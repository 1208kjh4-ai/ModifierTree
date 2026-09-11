using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class WorkingMultiPreviewChecks
{
    private const double Tolerance = 0.001;

    public static void Run()
    {
        MultipleRootsAndCache();
        NestedFrames();
        LegacyMirrorFrames();
    }

    private static void MultipleRootsAndCache()
    {
        using var fixture = new Fixture();
        var sourceA = fixture.Add(1, 0);
        var sourceB = fixture.Add(30, 3);
        var a = fixture.Array(sourceA, new ArraySettings { CountX = 3, Spacing = new(3, 10, 10), AxisX = new(1, 1, 0) });
        var b = fixture.Array(sourceB, new ArraySettings { CountX = 2, CountY = 2, Spacing = new(6, -4, 2), AxisY = new(0, 1, 1) });
        fixture.Evaluate();
        var settingsA = fixture.Tree.Find(a)!.Array;
        var settingsB = fixture.Tree.Find(b)!.Array;
        using var live = new LiveTreePreview();
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        var transforms = fixture.Transforms([a, b], rotation);
        Check(live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed,
                  transformNodeIds: [a, b]) && MatchesTransformed(fixture.Committed.Find(a)!, live.Find(a)!, rotation) &&
              MatchesTransformed(fixture.Committed.Find(b)!, live.Find(b)!, rotation),
            "Multiple working Array results rotate their independent arrangements and oblique axes live");
        Check(fixture.Tree.Find(a)!.Array == settingsA && fixture.Tree.Find(b)!.Array == settingsB,
            "Multi-result live rotation does not modify either committed Array frame");
        var rebuilds = live.RebuildCount;
        Check(!live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed,
                  transformNodeIds: [b, a, b]) && live.RebuildCount == rebuilds,
            "Reordered or duplicate working-result selections reuse the same live frame cache");
        Check(!live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, a, [b]),
            "Single-target and multi-target arguments combine into the same normalized selection set");
        Check(live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed,
                  transformNodeIds: [a]) && MatchesTransformed(fixture.Committed.Find(a)!, live.Find(a)!, rotation),
            "Changing only the working-result selection set invalidates cached Array frame ownership");
        using (var reference = fixture.InputOnly(transforms))
            Check(MatchesTransformed(reference.Find(b)!, live.Find(b)!, Transform.Identity) &&
                  !MatchesTransformed(fixture.Committed.Find(b)!, live.Find(b)!, rotation),
                "Removing a working result from frame ownership keeps that Array's axes fixed during input motion");

        Check(live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed),
            "Switching multi-result manipulation to input-only editing refreshes live frame ownership");
        using (var reference = fixture.InputOnly(transforms))
            Check(MatchesTransformed(reference.Find(a)!, live.Find(a)!, Transform.Identity) &&
                  MatchesTransformed(reference.Find(b)!, live.Find(b)!, Transform.Identity),
                "Moving all inputs without explicitly selected Modifiers never carries either Array frame");

        var secondRotation = Transform.Rotation(Math.PI, Vector3d.ZAxis, Point3d.Origin);
        transforms[fixture.ObjectId(sourceB)] = secondRotation;
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [a, b]);
        Check(MatchesTransformed(fixture.Committed.Find(a)!, live.Find(a)!, rotation) &&
              MatchesTransformed(fixture.Committed.Find(b)!, live.Find(b)!, secondRotation),
            "Independent working result transforms are evaluated against each result's absolute source transform");
        Check(live.Clear() && !live.IsActive && fixture.Tree.Find(a)!.Array == settingsA && fixture.Tree.Find(b)!.Array == settingsB,
            "Cancelling a multi-result drag discards every transient frame without changing saved settings");
    }

    private static void NestedFrames()
    {
        using var fixture = new Fixture();
        var sourceA = fixture.Add(1, 0);
        var sourceB = fixture.Add(30, 3);
        var inner = fixture.Array(sourceA, new ArraySettings { CountX = 2, Spacing = new(4, 5, 6) });
        var outer = fixture.Array(inner, new ArraySettings { CountX = 1, CountY = 2, Spacing = new(2, 7, 3) });
        var other = fixture.Array(sourceB, new ArraySettings { CountX = 2, Spacing = new(10, 5, 3) });
        fixture.Evaluate();
        using var live = new LiveTreePreview();
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        var transforms = fixture.Transforms([outer, other], rotation);
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [outer, other]);
        Check(MatchesTransformed(fixture.Committed.Find(outer)!, live.Find(outer)!, rotation) &&
              MatchesTransformed(fixture.Committed.Find(inner)!, live.Find(inner)!, rotation) &&
              MatchesTransformed(fixture.Committed.Find(other)!, live.Find(other)!, rotation),
            "Multi-result ancestor rotation carries nested Array frames alongside another independent root");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [outer, inner, other]);
        Check(MatchesTransformed(fixture.Committed.Find(outer)!, live.Find(outer)!, rotation),
            "Explicit ancestor and descendant targets carry each nested Array frame exactly once");

        var sibling = fixture.Add(80, 10);
        var parent = fixture.Modifier(TreeNodeKind.BooleanUnion, outer, sibling);
        fixture.Evaluate();
        transforms = fixture.Transforms([parent, other], rotation);
        transforms.Remove(fixture.ObjectId(sibling));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [parent, other]);
        using (var reference = fixture.InputOnly(transforms))
            Check(MatchesTransformed(reference.Find(outer)!, live.Find(outer)!, Transform.Identity) &&
                  MatchesTransformed(fixture.Committed.Find(other)!, live.Find(other)!, rotation),
                "An incomplete ancestor transform leaves its nested axes fixed while a complete independent target carries its frame");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [parent, outer, other]);
        Check(MatchesTransformed(fixture.Committed.Find(outer)!, live.Find(outer)!, rotation) &&
              MatchesTransformed(fixture.Committed.Find(other)!, live.Find(other)!, rotation),
            "A fully transformed explicit child can carry its frame even when another explicit ancestor is incomplete");
    }

    private static void LegacyMirrorFrames()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(1, 0);
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        var settings = new MirrorSettings(new ModifierVector(3, 2, 1), new ModifierVector(1, 0, 0), Union: false);
        if (!fixture.Tree.SetMirrorSettings(mirror, settings, out var error)) throw new Exception(error);
        fixture.Evaluate();
        using var live = new LiveTreePreview();
        var translation = Transform.Translation(8, 3, 0);
        var transforms = fixture.Transforms([mirror], translation);
        Check(live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, mirror) &&
              MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, translation),
            "Whole legacy Mirror translation carries its numerical fallback plane during live preview");
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        transforms = fixture.Transforms([mirror], rotation);
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [mirror]);
        Check(MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, rotation) &&
              fixture.Tree.Find(mirror)!.Mirror == settings,
            "Whole legacy Mirror rotation carries its plane without changing committed numerical settings");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, source);
        using (var reference = fixture.InputOnly(transforms))
            Check(MatchesTransformed(reference.Find(mirror)!, live.Find(mirror)!, Transform.Identity) &&
                  !MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, rotation),
                "Switching legacy Mirror manipulation to input editing keeps its fallback plane fixed");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [mirror]);
        Check(MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, rotation),
            "Re-selecting the whole legacy Mirror restores its transient plane at an unchanged drag transform");

        var sibling = fixture.Add(80, 10);
        var parent = fixture.Modifier(TreeNodeKind.BooleanUnion, mirror, sibling);
        fixture.Evaluate();
        transforms = fixture.Transforms([parent], rotation);
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, parent);
        Check(MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, rotation),
            "A whole ancestor live transform carries descendant legacy Mirror planes");
        transforms.Remove(fixture.ObjectId(sibling));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, parent);
        using (var reference = fixture.InputOnly(transforms))
            Check(MatchesTransformed(reference.Find(mirror)!, live.Find(mirror)!, Transform.Identity),
                "Removing an ancestor sibling transform restores a nested legacy plane even when its input transform is unchanged");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Committed, transformNodeIds: [parent, mirror]);
        Check(MatchesTransformed(fixture.Committed.Find(mirror)!, live.Find(mirror)!, rotation),
            "A fully transformed explicit legacy Mirror retains its plane when another selected ancestor is incomplete");
        Check(live.Clear() && fixture.Tree.Find(mirror)!.Mirror == settings,
            "Cancelling legacy Mirror live manipulation discards fallback plane overlays");

        using var evaluator = new TreeEvaluator();
        evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance,
            mirrorPlanes: id => id == mirror ? throw new InvalidOperationException("Expected plane-overlay failure") : null);
        Check(evaluator.Find(mirror) is { IsCurrent: false, HasResult: true, Error: "Expected plane-overlay failure" },
            "A failed legacy Mirror plane overlay reports a recoverable evaluation failure with its last valid result");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<Guid, GeometryBase> _sources = [];
        public ModifierTreeModel Tree { get; } = new();
        public TreeEvaluator Committed { get; } = new();
        public GeometryBase? Source(Guid id) => _sources.GetValueOrDefault(id);
        public Guid ObjectId(Guid nodeId) => Tree.Find(nodeId)!.ObjectId!.Value;
        public void Evaluate() => Committed.Rebuild(Tree, Source, Tolerance);
        public Guid Add(double x, double y)
        {
            var id = Guid.NewGuid();
            _sources.Add(id, new BoundingBox(x, y, 0, x + 1, y + 1, 1).ToBrep());
            Tree.RegisterSource(id);
            return Tree.FindSource(id)!.Id;
        }
        public Guid Modifier(TreeNodeKind kind, params Guid[] children)
        {
            var node = Tree.AddModifier(kind);
            foreach (var child in children)
                if (!Tree.Move(child, node.Id, node.Children.Count, out var error)) throw new Exception(error);
            return node.Id;
        }
        public Guid Array(Guid child, ArraySettings settings)
        {
            var id = Modifier(TreeNodeKind.Array, child);
            if (!Tree.SetArraySettings(id, settings, out var error)) throw new Exception(error);
            return id;
        }
        public Dictionary<Guid, Transform> Transforms(IEnumerable<Guid> nodes, Transform transform) =>
            nodes.SelectMany(Tree.SourcesInSubtree).Distinct().ToDictionary(id => id, _ => transform);
        public TreeEvaluator InputOnly(IReadOnlyDictionary<Guid, Transform> transforms)
        {
            var result = new TreeEvaluator();
            var moved = new Dictionary<Guid, GeometryBase>();
            try
            {
                foreach (var pair in _sources)
                {
                    var copy = pair.Value.Duplicate();
                    moved.Add(pair.Key, copy);
                    if (transforms.TryGetValue(pair.Key, out var transform)) copy.Transform(transform);
                }
                result.Rebuild(Tree, id => moved.GetValueOrDefault(id), Tolerance);
                return result;
            }
            catch { result.Dispose(); throw; }
            finally { foreach (var geometry in moved.Values) geometry.Dispose(); }
        }
        public void Dispose()
        {
            Committed.Dispose();
            foreach (var geometry in _sources.Values) geometry.Dispose();
        }
    }

    private static bool MatchesTransformed(NodeEvaluation baseline, NodeEvaluation actual, Transform transform)
    {
        if (!baseline.IsCurrent || !actual.IsCurrent || baseline.Results.Count != actual.Results.Count) return false;
        for (var i = 0; i < baseline.Results.Count; i++)
        {
            using var expected = baseline.Results[i].DuplicateBrep();
            if (!expected.Transform(transform)) return false;
            var expectedBounds = expected.GetBoundingBox(true);
            var actualBounds = actual.Results[i].GetBoundingBox(true);
            if (expectedBounds.Min.DistanceTo(actualBounds.Min) > Tolerance ||
                expectedBounds.Max.DistanceTo(actualBounds.Max) > Tolerance ||
                Math.Abs(expected.GetVolume() - actual.Results[i].GetVolume()) > Tolerance) return false;
        }
        return true;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
