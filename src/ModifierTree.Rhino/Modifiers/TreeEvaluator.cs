using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

internal sealed class NodeEvaluation : IDisposable
{
    public IReadOnlyList<Brep> Results { get; private set; } = Array.Empty<Brep>();
    public bool HasResult { get; private set; }
    public bool IsCurrent { get; private set; }
    public string? Error { get; private set; }
    public string Status { get; private set; } = "Pending";

    public void Succeed(Brep[] results)
    {
        Dispose();
        Results = results;
        HasResult = IsCurrent = true;
        Error = null;
        Status = results.Length == 0 ? "Empty" : $"Ready ({results.Length})";
    }

    public void Fail(string status, string error)
    {
        IsCurrent = false;
        Status = status;
        Error = error;
    }

    public NodeEvaluation Duplicate()
    {
        var copy = new NodeEvaluation();
        if (HasResult) copy.Succeed(Results.Select(brep => brep.DuplicateBrep()).ToArray());
        copy.IsCurrent = IsCurrent;
        copy.Error = Error;
        copy.Status = Status;
        return copy;
    }

    public void Dispose()
    {
        foreach (var brep in Results) brep.Dispose();
        Results = Array.Empty<Brep>();
        HasResult = IsCurrent = false;
    }
}

internal sealed record InputPreview(Guid ObjectId, Brep Geometry, bool IsCutter);

/// <summary>Evaluates ordered children recursively. Keeps stale results only across source edits.</summary>
internal sealed class TreeEvaluator : IDisposable
{
    private readonly Dictionary<Guid, NodeEvaluation> _evaluations = [];
    private long _revision = -1;
    private double _tolerance;
    private readonly List<NodeEvaluation> _roots = [];
    private readonly List<InputPreview> _inputs = [];
    public IReadOnlyList<NodeEvaluation> RootResults => _roots;
    public IReadOnlyList<InputPreview> InputPreviews => _inputs;
    public int RebuildCount { get; private set; }
    public int LastBooleanCount { get; private set; }
    public int LastEvaluatedNodeCount { get; private set; }
    public int LastReusedModifierCount { get; private set; }
    public NodeEvaluation? Find(Guid nodeId) => _evaluations.GetValueOrDefault(nodeId);
    public bool IsSnapshotOf(ModifierTreeModel tree, double tolerance) => _revision == tree.Revision && _tolerance == tolerance;

    public void Reset()
    {
        _roots.Clear();
        _inputs.Clear();
        foreach (var result in _evaluations.Values) result.Dispose();
        _evaluations.Clear();
        _revision = -1;
    }

    public void CopyFrom(TreeEvaluator baseline)
    {
        Reset();
        _revision = baseline._revision;
        _tolerance = baseline._tolerance;
        foreach (var pair in baseline._evaluations) _evaluations.Add(pair.Key, pair.Value.Duplicate());
    }

    public void Rebuild(ModifierTreeModel tree, Func<Guid, GeometryBase?> source, double tolerance,
        IReadOnlySet<Guid>? dirtyNodes = null, Func<Guid, Brep[]?>? reuseModifier = null,
        Func<Guid, ArraySettings?>? arraySettings = null, Func<Guid, Plane?>? mirrorPlanes = null)
    {
        if (!IsSnapshotOf(tree, tolerance)) { Reset(); dirtyNodes = null; }
        _revision = tree.Revision;
        _tolerance = tolerance;
        RebuildCount++;
        LastBooleanCount = LastEvaluatedNodeCount = LastReusedModifierCount = 0;
        _roots.Clear();
        _inputs.Clear();

        NodeEvaluation Evaluate(Guid id)
        {
            var node = tree.Find(id)!;
            if (dirtyNodes is not null && !dirtyNodes.Contains(id) && _evaluations.TryGetValue(id, out var cached)) return cached;
            LastEvaluatedNodeCount++;
            if (!_evaluations.TryGetValue(id, out var current))
                _evaluations.Add(id, current = new NodeEvaluation());
            try
            {
                if (node.ObjectId is { } objectId)
                {
                    // Missing/invalid source geometry cannot be used as an input cache.
                    current.Dispose();
                    if (node.IsControl)
                    {
                        var control = source(objectId);
                        if (node.Kind == TreeNodeKind.ControlBox &&
                            !ControlBoxGeometry.TryGetBox(control, tolerance, out _, out var controlError))
                            current.Fail("Invalid box", controlError);
                        else if (node.Kind == TreeNodeKind.BasePlane && !MirrorPlaneGeometry.TryGetPlane(control, tolerance, out _))
                            current.Fail("Invalid plane", "BasePlane is missing or is no longer a single planar surface. Use Set Plane to repair it.");
                        else current.Succeed([((Brep)control!).DuplicateBrep()]);
                        return current;
                    }
                    var geometry = DifferenceEvaluator.DuplicateInput(source(objectId));
                    var error = DifferenceEvaluator.Validate(geometry, "Source");
                    if (error is not null)
                    {
                        geometry?.Dispose();
                        current.Fail("Invalid input", error);
                    }
                    else current.Succeed([geometry!]);
                    return current;
                }

                // Control geometry is evaluated for editing/picking, but never contributes
                // to a modifier's input count, operand order, generated solids or bypass.
                var controls = node.Children.Where(child => tree.Find(child)!.IsControl).Select(Evaluate).ToArray();
                var children = node.Children.Where(child => !tree.Find(child)!.IsControl).Select(Evaluate).ToArray();
                var minimumInputs = !node.Enabled || node.Kind is TreeNodeKind.Mirror or TreeNodeKind.Array or TreeNodeKind.Bend ? 1 : 2;
                if (children.Length < minimumInputs)
                {
                    current.Dispose();
                    current.Fail("Needs inputs", $"Drag at least {(minimumInputs == 1 ? "one input" : "two inputs")} into this Modifier.");
                }
                else if (!node.Enabled)
                {
                    // Evaluate the remaining children for their own editing/status display, but
                    // only the first child's current geometry participates in this expression.
                    // A disabled node must never retain an old operation result as a fallback.
                    current.Dispose();
                    if (!children[0].IsCurrent)
                        current.Fail("Blocked", "The first input is missing, invalid, or waiting for its own inputs.");
                    else current.Succeed(children[0].Results.Select(result => result.DuplicateBrep()).ToArray());
                }
                else if (children.Any(child => !child.IsCurrent))
                    current.Fail("Blocked", "An input is missing, invalid, or waiting for its own inputs.");
                else if (node.Kind == TreeNodeKind.Bend && controls.Length == 0)
                    current.Fail("Needs Control Box", "Add a Control Box to this Bend.");
                else if (controls.Any(control => !control.IsCurrent))
                    current.Fail(node.Kind == TreeNodeKind.Bend ? "Invalid box" : "Invalid plane",
                        controls.First(control => !control.IsCurrent).Error ?? "A control object is missing or invalid.");
                else if (reuseModifier?.Invoke(id) is { } reused)
                {
                    current.Succeed(reused);
                    LastReusedModifierCount++;
                }
                else
                {
                    var inputs = children.Select(child => child.Results).ToArray();
                    if (node.Kind == TreeNodeKind.Bend)
                    {
                        var controlNodes = node.Children.Select(child => tree.Find(child)!).Where(child => child.IsControl).ToArray();
                        var bendControls = new BendControl[controlNodes.Length];
                        for (var i = 0; i < bendControls.Length; i++)
                        {
                            if (!ControlBoxGeometry.TryGetBox(controls[i].Results.Single(), tolerance, out var box, out var error))
                                throw new InvalidOperationException(error);
                            bendControls[i] = new BendControl(box, controlNodes[i].ControlBox!);
                        }
                        current.Succeed(BendEvaluator.Evaluate(inputs, bendControls, tolerance));
                    }
                    else if (node.Kind is TreeNodeKind.Mirror or TreeNodeKind.Array)
                    {
                        Plane? plane = null;
                        if (node.Kind == TreeNodeKind.Mirror && controls.Length > 0)
                        {
                            if (!MirrorPlaneGeometry.TryGetPlane(controls[0].Results.Single(), tolerance, out var value))
                                throw new InvalidOperationException("BasePlane must be a single planar surface.");
                            plane = value;
                        }
                        else if (node.Kind == TreeNodeKind.Mirror)
                        {
                            plane = mirrorPlanes?.Invoke(id);
                            if (plane is { IsValid: false })
                                throw new InvalidOperationException("A Mirror plane cannot accept this transformation.");
                        }
                        if (node.Kind == TreeNodeKind.Mirror && node.Mirror!.Union &&
                            inputs.Sum(input => input.Count) * (node.Mirror.KeepOriginal ? 2 : 1) > 1) LastBooleanCount++;
                        current.Succeed(PatternEvaluator.Evaluate(node, inputs, tolerance, plane, arraySettings?.Invoke(id)));
                    }
                    else
                    {
                        LastBooleanCount++;
                        current.Succeed(BooleanEvaluator.Evaluate(node.Kind, inputs, tolerance));
                    }
                }
            }
            catch (Exception exception) { current.Fail("FAILED", exception.Message); }
            return current;
        }

        void CollectInputs(Guid id, bool cutter)
        {
            var node = tree.Find(id)!;
            if (node.IsControl) return;
            if (node.ObjectId is { } objectId)
            {
                var value = Find(id);
                if (value?.IsCurrent == true)
                    foreach (var brep in value.Results) _inputs.Add(new InputPreview(objectId, brep, cutter));
            }
            else
            {
                var inputs = node.Children.Where(child => !tree.Find(child)!.IsControl).ToArray();
                for (var i = 0; i < inputs.Length; i++)
                    CollectInputs(inputs[i], cutter ||
                        (node.Kind == TreeNodeKind.BooleanDifference || !node.Enabled) && i > 0);
            }
        }

        foreach (var id in tree.Roots)
        {
            if (!tree.Find(id)!.IsModifier) continue;
            var evaluation = Evaluate(id);
            _roots.Add(evaluation);
            // If the root is blocked, show the real sources alongside the stale result.
            if (evaluation.IsCurrent) CollectInputs(id, false);
        }
    }

    public void Dispose() => Reset();
}
