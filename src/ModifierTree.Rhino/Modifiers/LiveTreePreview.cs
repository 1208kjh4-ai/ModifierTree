using System.Diagnostics;
using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>One drag owns its caches; absolute transforms never accumulate or modify committed results.</summary>
internal sealed class LiveTreePreview : IDisposable
{
    private readonly TreeEvaluator _evaluator = new();
    private readonly TreeEvaluator _fallbackBaseline = new();
    private TreeEvaluator? _baseline;
    private int _baselineGeneration;
    private Dictionary<Guid, Transform> _transforms = [];
    private Dictionary<Guid, ArraySettings> _arrayFrames = [];
    private Dictionary<Guid, string> _frameErrors = [];
    private Dictionary<Guid, Plane> _mirrorPlanes = [];
    private Dictionary<Guid, string> _mirrorErrors = [];
    private long _revision = -1;
    private double _tolerance;
    private Guid[] _transformNodeIds = [];
    public bool IsActive { get; private set; }
    public int RebuildCount => _evaluator.RebuildCount;
    public int LastBooleanCount { get; private set; }
    public int LastEvaluatedNodeCount { get; private set; }
    public int LastReusedModifierCount { get; private set; }
    public double LastUpdateMilliseconds { get; private set; }
    public NodeEvaluation? Find(Guid id) => IsActive ? _evaluator.Find(id) : null;

    public bool Update(ModifierTreeModel tree, Func<Guid, GeometryBase?> source,
        IReadOnlyDictionary<Guid, Transform> transforms, double tolerance, TreeEvaluator? committed = null,
        Guid? transformNodeId = null, IReadOnlyCollection<Guid>? transformNodeIds = null)
    {
        if (transforms.Count == 0) return Clear();
        // Native selection may contain several independent working results. Normalize
        // the set so duplicate IDs or a different native selection order do not reset a drag.
        var targets = (transformNodeIds ?? []).Concat(transformNodeId is { } single ? [single] : [])
            .Distinct().Order().ToArray();
        if (committed is not null && !committed.IsSnapshotOf(tree, tolerance)) committed = null;
        var reset = !IsActive || _revision != tree.Revision || _tolerance != tolerance || !_transformNodeIds.SequenceEqual(targets) ||
            committed is not null && (!ReferenceEquals(_baseline, committed) || _baselineGeneration != committed.RebuildCount);
        if (!reset && transforms.Count == _transforms.Count &&
            transforms.All(pair => _transforms.TryGetValue(pair.Key, out var previous) && previous == pair.Value)) return false;
        var started = Stopwatch.GetTimestamp();
        var baselineBooleans = 0;
        if (reset)
        {
            if (committed is null)
            {
                _fallbackBaseline.Rebuild(tree, source, tolerance);
                baselineBooleans = _fallbackBaseline.LastBooleanCount;
                committed = _fallbackBaseline;
            }
            _baseline = committed;
            _baselineGeneration = committed.RebuildCount;
            _evaluator.CopyFrom(committed);
            _transforms.Clear();
            _arrayFrames.Clear();
            _frameErrors.Clear();
            _mirrorPlanes.Clear();
            _mirrorErrors.Clear();
        }
        _revision = tree.Revision;
        _tolerance = tolerance;
        _transformNodeIds = targets;

        // Include removed transforms so a source returning to rest invalidates its ancestors too.
        var dirty = new HashSet<Guid>();
        foreach (var objectId in _transforms.Keys.Union(transforms.Keys))
        {
            if (_transforms.TryGetValue(objectId, out var previous) && transforms.TryGetValue(objectId, out var next) && previous == next) continue;
            for (var node = tree.FindSource(objectId); node is not null; node = node.ParentId is { } parent ? tree.Find(parent) : null)
                dirty.Add(node.Id);
        }

        var uniformTransforms = new Dictionary<Guid, Transform?>();
        Transform? Uniform(Guid id)
        {
            if (uniformTransforms.TryGetValue(id, out var cached)) return cached;
            var node = tree.Find(id)!;
            var children = node.Enabled ? node.Children : node.Children.Where(child => !tree.Find(child)!.IsControl).Take(1).ToArray();
            Transform? value = node.ObjectId is { } objectId
                ? transforms.TryGetValue(objectId, out var transform) ? transform : Transform.Identity
                : children.Count > 0 ? Uniform(children[0]) : null;
            if (node.IsModifier && node.Enabled && children.Any(child => Uniform(child) != value)) value = null;
            uniformTransforms[id] = value;
            return value;
        }

        var translationCompatible = new Dictionary<Guid, bool>();
        bool CanTranslateResult(Guid id)
        {
            if (translationCompatible.TryGetValue(id, out var cached)) return cached;
            var node = tree.Find(id)!;
            // Legacy Mirrors use optional plane overlays and take the evaluation path.
            // With BasePlane, Uniform additionally verifies the control moves with
            // every source, so translating the committed result is exact again.
            var compatible = !node.IsModifier ||
                (node.Enabled
                    ? (node.Kind != TreeNodeKind.Mirror || node.Children.Any(child => tree.Find(child)!.IsControl)) && node.Children.All(CanTranslateResult)
                    : node.Children.FirstOrDefault(child => !tree.Find(child)!.IsControl) is var first && first != Guid.Empty && CanTranslateResult(first));
            translationCompatible[id] = compatible;
            return compatible;
        }

        Brep[]? Reuse(Guid id)
        {
            // Translation preserves the Boolean and the document's absolute tolerance.
            // Scale and other transforms deliberately take the normal evaluation path.
            if (!CanTranslateResult(id) || Uniform(id) is not { } transform || !transform.IsValid ||
                transform != Transform.Translation(transform.M03, transform.M13, transform.M23) ||
                _baseline!.Find(id) is not { IsCurrent: true, HasResult: true } baseline) return null;
            var copies = new List<Brep>();
            try
            {
                foreach (var brep in baseline.Results)
                {
                    var copy = brep.DuplicateBrep();
                    copies.Add(copy);
                    if (!copy.Transform(transform)) throw new InvalidOperationException("Could not translate a cached result.");
                }
                return copies.ToArray();
            }
            catch
            {
                foreach (var copy in copies) copy.Dispose();
                throw;
            }
        }

        var movedSources = new Dictionary<Guid, GeometryBase>();
        try
        {
            var arrayFrames = new Dictionary<Guid, ArraySettings>();
            var frameErrors = new Dictionary<Guid, string>();
            var mirrorPlanes = new Dictionary<Guid, Plane>();
            var mirrorErrors = new Dictionary<Guid, string>();
            foreach (var selected in targets)
            {
                if (tree.Find(selected)?.IsModifier != true) continue;
                // Only explicitly manipulated modifiers carry their frames. Moving
                // its one remaining input in edit scope must not rotate the Array axes.
                // Match the commit tracker: incomplete ancestor transforms do not carry
                // a descendant frame, even when all of that descendant's inputs moved.
                var owned = tree.SourcesInSubtree(selected);
                if (owned.Count > 0 && transforms.TryGetValue(owned[0], out var transform) &&
                    owned.All(id => transforms.TryGetValue(id, out var other) && other == transform))
                {
                    foreach (var array in PatternFrameTransform.ArraysInSubtree(tree, selected))
                    {
                        // An ancestor and descendant may both be supplied by a caller.
                        // A fully transformed ancestor already owns this absolute frame.
                        if (arrayFrames.ContainsKey(array.Id) || frameErrors.ContainsKey(array.Id)) continue;
                        if (!PatternFrameTransform.TryTransform(array.Array!, transform, out var settings, out var error))
                            frameErrors.Add(array.Id, error);
                        else arrayFrames.Add(array.Id, settings);
                    }
                    foreach (var mirror in LegacyMirrorsInSubtree(tree, selected))
                    {
                        if (mirrorPlanes.ContainsKey(mirror.Id) || mirrorErrors.ContainsKey(mirror.Id)) continue;
                        if (!TryTransformMirrorPlane(mirror.Mirror!, transform, out var plane, out var error))
                            mirrorErrors.Add(mirror.Id, error);
                        else mirrorPlanes.Add(mirror.Id, plane);
                    }
                }
            }
            foreach (var id in _arrayFrames.Keys.Union(arrayFrames.Keys).Union(_frameErrors.Keys).Union(frameErrors.Keys))
            {
                if (_arrayFrames.GetValueOrDefault(id) == arrayFrames.GetValueOrDefault(id) &&
                    _frameErrors.GetValueOrDefault(id) == frameErrors.GetValueOrDefault(id)) continue;
                for (var node = tree.Find(id); node is not null; node = node.ParentId is { } parent ? tree.Find(parent) : null)
                    dirty.Add(node.Id);
            }
            foreach (var id in _mirrorPlanes.Keys.Union(mirrorPlanes.Keys).Union(_mirrorErrors.Keys).Union(mirrorErrors.Keys))
            {
                if (_mirrorPlanes.GetValueOrDefault(id) == mirrorPlanes.GetValueOrDefault(id) &&
                    _mirrorErrors.GetValueOrDefault(id) == mirrorErrors.GetValueOrDefault(id)) continue;
                for (var node = tree.Find(id); node is not null; node = node.ParentId is { } parent ? tree.Find(parent) : null)
                    dirty.Add(node.Id);
            }
            GeometryBase? Read(Guid id)
            {
                if (movedSources.TryGetValue(id, out var moved)) return moved;
                var geometry = source(id);
                if (geometry is null || !transforms.TryGetValue(id, out var transform)) return geometry;
                var copy = geometry.Duplicate();
                movedSources.Add(id, copy);
                return transform.IsValid && copy.Transform(transform) ? copy : null;
            }
            Plane? ReadMirrorPlane(Guid id)
            {
                if (mirrorErrors.TryGetValue(id, out var error)) throw new InvalidOperationException(error);
                return mirrorPlanes.TryGetValue(id, out var plane) ? plane : null;
            }
            _evaluator.Rebuild(tree, Read, tolerance, dirty, Reuse, id => frameErrors.TryGetValue(id, out var error)
                ? throw new InvalidOperationException(error) : arrayFrames.GetValueOrDefault(id), ReadMirrorPlane);
            _transforms = transforms.ToDictionary(pair => pair.Key, pair => pair.Value);
            _arrayFrames = arrayFrames;
            _frameErrors = frameErrors;
            _mirrorPlanes = mirrorPlanes;
            _mirrorErrors = mirrorErrors;
            IsActive = true;
            LastBooleanCount = baselineBooleans + _evaluator.LastBooleanCount;
            LastEvaluatedNodeCount = _evaluator.LastEvaluatedNodeCount;
            LastReusedModifierCount = _evaluator.LastReusedModifierCount;
            return true;
        }
        finally
        {
            foreach (var copy in movedSources.Values) copy.Dispose();
            LastUpdateMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
    }

    private static IEnumerable<ModifierTreeNode> LegacyMirrorsInSubtree(ModifierTreeModel tree, Guid id)
    {
        if (tree.Find(id) is not { IsModifier: true } node) yield break;
        if (node.Kind == TreeNodeKind.Mirror && !node.Children.Any(child => tree.Find(child)!.IsControl)) yield return node;
        foreach (var child in node.Children)
        foreach (var mirror in LegacyMirrorsInSubtree(tree, child)) yield return mirror;
    }

    private static bool TryTransformMirrorPlane(MirrorSettings settings, Transform transform, out Plane plane, out string error)
    {
        plane = Plane.Unset;
        error = "A Mirror plane cannot accept this transformation.";
        try
        {
            plane = new Plane(new Point3d(settings.Origin.X, settings.Origin.Y, settings.Origin.Z),
                new Vector3d(settings.Normal.X, settings.Normal.Y, settings.Normal.Z));
            if (!transform.IsValid || !transform.IsAffine || !transform.TryGetInverse(out _) ||
                !plane.Transform(transform) || !plane.IsValid) return false;
            var candidate = settings with
            {
                Origin = new ModifierVector(plane.OriginX, plane.OriginY, plane.OriginZ),
                Normal = new ModifierVector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z)
            };
            return ModifierSettings.TryValidate(candidate, out error);
        }
        catch (Exception exception)
        {
            // Evaluation handles this value through its normal stale-result failure path.
            error += " " + exception.Message;
            return false;
        }
    }

    public bool Clear()
    {
        var changed = IsActive;
        IsActive = false;
        _transforms.Clear();
        _arrayFrames.Clear();
        _frameErrors.Clear();
        _mirrorPlanes.Clear();
        _mirrorErrors.Clear();
        _evaluator.Reset();
        _fallbackBaseline.Reset();
        _baseline = null;
        _revision = -1;
        _transformNodeIds = [];
        return changed;
    }

    public void Dispose() => Clear();
}
