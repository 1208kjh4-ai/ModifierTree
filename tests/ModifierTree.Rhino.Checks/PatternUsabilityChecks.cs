using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class PatternUsabilityChecks
{
    private const double Tolerance = 0.001;

    public static void Run()
    {
        MirrorUnion();
        MirrorControl();
        ArrayDirections();
        WholeArrayFrames();
    }

    private static void MirrorUnion()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(-1, 2));
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        fixture.Evaluate();
        Check(fixture.Result(mirror) is { IsCurrent: true, Results.Count: 1 } && Near(Volume(fixture.Result(mirror)), 4),
            "New Mirror unites overlapping original and reflected solids by default");
        fixture.Tree.SetMirrorSettings(mirror, MirrorSettings.Default with { Union = false }, out _);
        fixture.Evaluate();
        Check(fixture.Result(mirror).Results.Count == 2 && Near(Volume(fixture.Result(mirror)), 6),
            "Mirror Union OFF retains independent overlapping pieces");
        fixture.Tree.SetMirrorSettings(mirror, MirrorSettings.Default with { KeepOriginal = false }, out _);
        fixture.Evaluate();
        Check(fixture.Result(mirror).Results.Count == 1 && Near(Volume(fixture.Result(mirror)), 3),
            "Mirror Keep Original OFF does not union the source back into the reflection");
        fixture.Replace(source, Box(0, 2));
        fixture.Tree.SetMirrorSettings(mirror, MirrorSettings.Default, out _);
        fixture.Evaluate();
        Check(fixture.Result(mirror).Results.Count == 1 && Near(Volume(fixture.Result(mirror)), 4),
            "Mirror default Union joins solids meeting across the mirror plane");
        fixture.Replace(source, Box(1, 2));
        fixture.Evaluate();
        Check(fixture.Result(mirror).Results.Count == 2 && Near(Volume(fixture.Result(mirror)), 2),
            "Mirror Union keeps disconnected reflected regions as separate result pieces");
    }

    private static void MirrorControl()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        var control = fixture.Plane(mirror, new Plane(new Point3d(3, 0, 0), Vector3d.XAxis));
        fixture.Tree.SetMirrorSettings(mirror, MirrorSettings.Default with { KeepOriginal = false }, out _);
        fixture.Evaluate();
        Check(fixture.Result(control) is { IsCurrent: true, Results.Count: 1 } && !fixture.Result(control).Results[0].IsSolid &&
              Near(Bounds(fixture.Result(mirror)).Min.X, 4) && fixture.Result(mirror).Results.Count == 1,
            "Mirror takes its infinite plane from an editable open BasePlane surface, excluding it from output");
        Check(fixture.Evaluator.InputPreviews.Count == 1 && fixture.Evaluator.InputPreviews[0].ObjectId == fixture.ObjectId(source),
            "BasePlane is never classified as a geometric or cutter preview input");
        fixture.Tree.Move(control, mirror, fixture.Tree.Find(mirror)!.Children.Count, out _);
        fixture.Evaluate();
        Check(Near(Bounds(fixture.Result(mirror)).Min.X, 4), "Reordering BasePlane does not change its control role");

        using var live = new LiveTreePreview();
        var movePlane = new Dictionary<Guid, Transform> { [fixture.ObjectId(control)] = Transform.Translation(2, 0, 0) };
        live.Update(fixture.Tree, fixture.Source, movePlane, Tolerance, fixture.Evaluator, control);
        Check(Near(Bounds(live.Find(mirror)!).Min.X, 8) && Near(Bounds(fixture.Result(mirror)).Min.X, 4),
            "Moving only BasePlane updates the reflection live without moving the input or committed preview");
        var whole = fixture.Tree.SourcesInSubtree(mirror).ToDictionary(id => id, _ => Transform.Translation(2, 0, 0));
        live.Update(fixture.Tree, fixture.Source, whole, Tolerance, fixture.Evaluator, mirror);
        Check(Near(Bounds(live.Find(mirror)!).Min.X, 6) && live.LastReusedModifierCount == 1,
            "Whole Mirror movement includes BasePlane and safely translates its cached result");
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        whole = fixture.Tree.SourcesInSubtree(mirror).ToDictionary(id => id, _ => rotation);
        live.Update(fixture.Tree, fixture.Source, whole, Tolerance, fixture.Evaluator, mirror);
        Check(MatchesTransformed(fixture.Result(mirror), live.Find(mirror)!, rotation),
            "Whole Mirror rotation carries its plane and source as one rigid result");
        live.Clear();

        fixture.Replace(control, new LineCurve(Point3d.Origin, Point3d.Origin + Vector3d.XAxis));
        fixture.Evaluate();
        Check(fixture.Result(mirror) is { IsCurrent: false, HasResult: true, Status: "Invalid plane" },
            "An invalid BasePlane reports failure while retaining the last valid Mirror result");
        fixture.Tree.SetModifierEnabled(mirror, false, out _);
        fixture.Evaluate();
        Check(fixture.Result(mirror) is { IsCurrent: true, Results.Count: 1 } && Near(Bounds(fixture.Result(mirror)).Min.X, 1),
            "Disabled Mirror bypasses the first geometric input even when its BasePlane is invalid");
    }

    private static void ArrayDirections()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var array = fixture.Modifier(TreeNodeKind.Array, source);
        fixture.Tree.SetArraySettings(array, new ArraySettings
        {
            CountX = 2, CountY = 2, Spacing = new ModifierVector(3, -4, 7),
            AxisX = new ModifierVector(10, 0, 0), AxisY = new ModifierVector(1, 1, 0)
        }, out _);
        fixture.Evaluate();
        var result = fixture.Result(array);
        var centers = result.Results.Select(brep => brep.GetBoundingBox(true).Center).ToArray();
        var origin = new Point3d(1.5, 0.5, 0.5);
        var diagonal = new Vector3d(-4 / Math.Sqrt(2), -4 / Math.Sqrt(2), 0);
        Check(result.Results.Count == 4 && centers[0].DistanceTo(origin) < 1e-9 &&
              centers[1].DistanceTo(origin + new Vector3d(3, 0, 0)) < 1e-9 &&
              centers[2].DistanceTo(origin + diagonal) < 1e-9 &&
              centers[3].DistanceTo(origin + new Vector3d(3, 0, 0) + diagonal) < 1e-9,
            "Array normalizes independent oblique axes, preserves source origin and applies signed spacing");

        var initial = fixture.Tree.Find(array)!.Array!;
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        Check(PatternFrameTransform.TryTransform(initial, rotation, out var rotated, out _) &&
              Near(rotated.Spacing.X, initial.Spacing.X) && Near(rotated.Spacing.Y, initial.Spacing.Y) &&
              Near(rotated.AxisX.X, 0) && Near(rotated.AxisX.Y, 1),
            "Array frame rotation changes directions while retaining signed spacing");
        Check(PatternFrameTransform.TryTransform(initial, Transform.Translation(100, 200, 300), out var translated, out _) && translated == initial,
            "Array frame translation leaves every saved axis and spacing exactly unchanged");
        var affine = Transform.Scale(Plane.WorldXY, 2, 3, 4);
        Check(PatternFrameTransform.TryTransform(initial, affine, out var scaled, out _) &&
              Near(scaled.Spacing.X, 6) && Near(scaled.Spacing.Y, -4 * Math.Sqrt(6.5)),
            "Nonuniform scaling transfers transformed axis lengths into spacing");
        Check(!PatternFrameTransform.TryTransform(initial, Transform.Scale(Plane.WorldXY, 0, 1, 1), out _, out _),
            "Collapsed Array frame axes are rejected before changing saved settings");
    }

    private static void WholeArrayFrames()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var array = fixture.Modifier(TreeNodeKind.Array, source);
        fixture.Evaluate();
        using var live = new LiveTreePreview();
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        var transforms = new Dictionary<Guid, Transform> { [fixture.ObjectId(source)] = rotation };
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, array);
        Check(MatchesTransformed(fixture.Result(array), live.Find(array)!, rotation),
            "Rotating a whole Array rotates its copied arrangement live, including its saved axes");
        Check(fixture.Tree.Find(array)!.Array == ArraySettings.Default,
            "Live Array frame preview leaves committed settings unchanged until transform completion");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, source);
        var bounds = Bounds(live.Find(array)!);
        Check(Near(bounds.Max.X, 10) && Near(bounds.Max.Y, 2),
            "Rotating an individual Array input keeps the Array direction fixed even for one-input Arrays");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, array);
        var rotation2 = Transform.Rotation(Math.PI, Vector3d.ZAxis, Point3d.Origin);
        transforms[fixture.ObjectId(source)] = rotation2;
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, array);
        Check(MatchesTransformed(fixture.Result(array), live.Find(array)!, rotation2),
            "Consecutive whole-Array rotation frames use absolute transforms without cumulative axis drift");
        Check(live.Clear() && live.Find(array) is null && fixture.Tree.Find(array)!.Array == ArraySettings.Default,
            "Cancelling a whole Array rotation discards transient axes and result copies");

        var remote = fixture.Add(Box(30, 31));
        var parent = fixture.Modifier(TreeNodeKind.BooleanUnion, array, remote);
        fixture.Evaluate();
        transforms = fixture.Tree.SourcesInSubtree(parent).ToDictionary(id => id, _ => rotation);
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, parent);
        Check(MatchesTransformed(fixture.Result(parent), live.Find(parent)!, rotation),
            "Rotating an ancestor carries nested Array frames and sibling source geometry together");
        transforms.Remove(fixture.ObjectId(remote));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, parent);
        Check(Near(Bounds(live.Find(array)!).Max.X, 10),
            "A partial ancestor transform restores nested Array frames even if their input transforms are unchanged");
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, fixture.Evaluator, source);
        Check(Near(Bounds(live.Find(array)!).Max.X, 10),
            "Switching an ancestor drag to input editing restores nested Array axes");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<Guid, GeometryBase> _sources = [];
        public ModifierTreeModel Tree { get; } = new();
        public TreeEvaluator Evaluator { get; } = new();
        public GeometryBase? Source(Guid id) => _sources.GetValueOrDefault(id);
        public Guid ObjectId(Guid nodeId) => Tree.Find(nodeId)!.ObjectId!.Value;
        public NodeEvaluation Result(Guid nodeId) => Evaluator.Find(nodeId)!;
        public void Evaluate() => Evaluator.Rebuild(Tree, Source, Tolerance);
        public Guid Add(GeometryBase geometry)
        {
            var id = Guid.NewGuid(); _sources.Add(id, geometry); Tree.RegisterSource(id); return Tree.FindSource(id)!.Id;
        }
        public Guid Plane(Guid mirror, Plane plane)
        {
            var id = Guid.NewGuid();
            using var surface = new PlaneSurface(plane, new Interval(-5, 5), new Interval(-5, 5));
            _sources.Add(id, surface.ToBrep());
            if (!Tree.SetBasePlane(mirror, id, out var error)) throw new Exception(error);
            return Tree.FindSource(id)!.Id;
        }
        public Guid Modifier(TreeNodeKind kind, params Guid[] children)
        {
            var node = Tree.AddModifier(kind);
            foreach (var child in children)
                if (!Tree.Move(child, node.Id, node.Children.Count, out var error)) throw new Exception(error);
            return node.Id;
        }
        public void Replace(Guid nodeId, GeometryBase geometry)
        {
            var id = ObjectId(nodeId); _sources[id].Dispose(); _sources[id] = geometry;
        }
        public void Dispose() { Evaluator.Dispose(); foreach (var geometry in _sources.Values) geometry.Dispose(); }
    }

    private static bool MatchesTransformed(NodeEvaluation baseline, NodeEvaluation actual, Transform transform)
    {
        if (!actual.IsCurrent || baseline.Results.Count != actual.Results.Count) return false;
        var expectedBounds = BoundingBox.Empty;
        var expectedVolume = 0.0;
        foreach (var result in baseline.Results)
        {
            using var moved = result.DuplicateBrep();
            moved.Transform(transform);
            expectedBounds.Union(moved.GetBoundingBox(true));
            expectedVolume += moved.GetVolume();
        }
        var actualBounds = Bounds(actual);
        return actualBounds.Min.DistanceTo(expectedBounds.Min) < Tolerance && actualBounds.Max.DistanceTo(expectedBounds.Max) < Tolerance &&
            Near(expectedVolume, Volume(actual));
    }
    private static Brep Box(double first, double last) => new BoundingBox(first, 0, 0, last, 1, 1).ToBrep();
    private static double Volume(NodeEvaluation result) => result.Results.Sum(brep => brep.GetVolume());
    private static BoundingBox Bounds(NodeEvaluation result)
    {
        var bounds = BoundingBox.Empty;
        foreach (var brep in result.Results) bounds.Union(brep.GetBoundingBox(true));
        return bounds;
    }
    private static bool Near(double left, double right) => Math.Abs(left - right) < Tolerance;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
