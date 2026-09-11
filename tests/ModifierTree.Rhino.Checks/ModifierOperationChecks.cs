using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.Geometry;

internal static class ModifierOperationChecks
{
    private const double Tolerance = 0.001;

    public static void Run()
    {
        MirrorGeometry();
        ArrayGeometry();
        DisabledModifiers();
        NestedPatterns();
        OverlappingPatterns();
        LivePatterns();
        PatternUsabilityChecks.Run();
        foreach (var kind in new[] { TreeNodeKind.Mirror, TreeNodeKind.Array })
        foreach (var mode in new[] { ResultCommitMode.Bake, ResultCommitMode.Merge }) Commit(kind, mode);
    }

    private static void MirrorGeometry()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        using var evaluator = new TreeEvaluator();
        void Evaluate() => evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        void Settings(MirrorSettings settings)
        {
            if (!fixture.Tree.SetMirrorSettings(mirror, settings, out var error)) throw new Exception(error);
            Evaluate();
        }
        Evaluate();
        var current = evaluator.Find(mirror)!;
        Check(Current(current, 2, 2) && Bounds(current).Min.X == -2 && Bounds(current).Max.X == 2,
            "Mirror keeps the original by default and reflects across the world YZ plane");
        Settings(MirrorSettings.Default with { KeepOriginal = false });
        current = evaluator.Find(mirror)!;
        Check(Current(current, 1, 1) && Bounds(current).Min.X == -2 && Bounds(current).Max.X == -1,
            "Mirror can return only its reflected result");
        Settings(new MirrorSettings(new ModifierVector(5, 0, 0), new ModifierVector(2, 0, 0), false));
        Check(Current(evaluator.Find(mirror)!, 1, 1) && Near(Bounds(evaluator.Find(mirror)!).Min.X, 8),
            "Mirror uses a world-space plane origin and normalizes its normal");
        Settings(new MirrorSettings(new ModifierVector(0, 0, 0), new ModifierVector(1, 1, 0), false));
        var bounds = Bounds(evaluator.Find(mirror)!);
        Check(Near(bounds.Min.X, -1) && Near(bounds.Max.X, 0) && Near(bounds.Min.Y, -2) && Near(bounds.Max.Y, -1),
            "Mirror supports an oblique plane normal");
        Check(evaluator.Find(mirror)!.Results.All(brep => brep.SolidOrientation == BrepSolidOrientation.Outward),
            "A reflected result retains outward solid orientation");

        fixture.Geometry(source).Flip();
        Evaluate();
        Check(Current(evaluator.Find(mirror)!, 1, 1) && fixture.Geometry(source).SolidOrientation == BrepSolidOrientation.Inward &&
              Near(fixture.Geometry(source).GetBoundingBox(true).Min.X, 1),
            "Mirror normalizes its result without modifying the source orientation or position");
        var second = fixture.Add(Box(4, 5));
        fixture.Move(second, mirror);
        Settings(MirrorSettings.Default);
        Check(Current(evaluator.Find(mirror)!, 4, 4), "Mirror transforms every child input as a set");

        using var hollowFixture = new Fixture();
        using var outer = Box(0, 10, 0, 10, 0, 10);
        using var inner = Box(2, 8, 2, 8, 2, 8);
        inner.Flip();
        var hollow = hollowFixture.Add(ResultGeometry.Create(new[] { outer, inner }));
        var hollowMirror = hollowFixture.Modifier(TreeNodeKind.Mirror, hollow);
        hollowFixture.Tree.SetMirrorSettings(hollowMirror, MirrorSettings.Default with { KeepOriginal = false }, out _);
        evaluator.Rebuild(hollowFixture.Tree, hollowFixture.Source, Tolerance);
        var result = evaluator.Find(hollowMirror)!;
        var shells = result.Results.Single().GetConnectedComponents();
        try
        {
            Check(Current(result, 1, 784) && shells.Count(shell => shell.SolidOrientation == BrepSolidOrientation.Inward) == 1,
                "Mirror preserves a compound solid's inward cavity shell and its material volume");
        }
        finally { foreach (var shell in shells) shell.Dispose(); }
    }

    private static void ArrayGeometry()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var array = fixture.Modifier(TreeNodeKind.Array, source);
        using var evaluator = new TreeEvaluator();
        void Settings(ArraySettings settings)
        {
            if (!fixture.Tree.SetArraySettings(array, settings, out var error)) throw new Exception(error);
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        }
        Settings(new ArraySettings { CountX = 2, CountY = 3, CountZ = 2, Spacing = new ModifierVector(3, 4, 5) });
        var current = evaluator.Find(array)!;
        var bounds = Bounds(current);
        Check(Current(current, 12, 12) && bounds.Min == new Point3d(1, 0, 0) && bounds.Max == new Point3d(5, 9, 6),
            "Array counts include the original and create a complete X/Y/Z grid");
        Settings(new ArraySettings { CountX = 3, Spacing = new ModifierVector(-3, 0, 0) });
        Check(Current(evaluator.Find(array)!, 3, 3) && Bounds(evaluator.Find(array)!).Min.X == -5,
            "Linear Array supports negative spacing");
        Settings(new ArraySettings { CountX = 3, Spacing = new ModifierVector(0, 0, 0) });
        Check(Current(evaluator.Find(array)!, 3, 3) && evaluator.Find(array)!.Results.All(result => result.GetBoundingBox(true).Min.X == 1),
            "Overlapping Array copies remain separate without an implicit Boolean Union");
        Settings(new ArraySettings { CountX = 1 });
        Check(Current(evaluator.Find(array)!, 1, 1) && !ReferenceEquals(evaluator.Find(array)!.Results[0], fixture.Geometry(source)),
            "An Array with one instance still owns an independent output copy");
        var second = fixture.Add(Box(4, 5));
        fixture.Move(second, array);
        Settings(new ArraySettings { CountX = 3, Spacing = new ModifierVector(10, 0, 0) });
        Check(Current(evaluator.Find(array)!, 6, 6), "Array repeats all children at every grid position");
        Check(Near(fixture.Geometry(source).GetBoundingBox(true).Min.X, 1) && Near(fixture.Geometry(second).GetBoundingBox(true).Min.X, 4),
            "Array preserves the document input geometry");

        var node = fixture.Tree.Find(array)!;
        fixture.Tree.SetArraySettings(array, new ArraySettings { CountX = 256 }, out _);
        try
        {
            var results = PatternEvaluator.Evaluate(node, [Enumerable.Repeat(fixture.Geometry(source), 17).ToArray()]);
            foreach (var result in results) result.Dispose();
            throw new Exception("FAIL: A nested pattern expansion exceeded the piece budget without failing.");
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains("4,096"), "Pattern expansion rejects excessive result pieces before copying geometry");
        }
    }

    private static void DisabledModifiers()
    {
        foreach (var kind in new[] { TreeNodeKind.BooleanDifference, TreeNodeKind.BooleanUnion, TreeNodeKind.BooleanIntersection,
                     TreeNodeKind.Mirror, TreeNodeKind.Array })
        {
            using var fixture = new Fixture();
            var source = fixture.Add(Box(0, 10));
            var invalid = fixture.Add(new LineCurve(Point3d.Origin, new Point3d(1, 0, 0)));
            var modifier = fixture.Modifier(kind, source, invalid);
            fixture.Tree.SetModifierEnabled(modifier, false, out _);
            using var evaluator = new TreeEvaluator();
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
            Check(Current(evaluator.Find(modifier)!, 1, 10) && evaluator.Find(invalid) is { IsCurrent: false },
                $"Disabled {kind} passes its first input through despite an invalid later child");
            fixture.Tree.SetModifierEnabled(modifier, true, out _);
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
            Check(evaluator.Find(modifier) is { IsCurrent: false, HasResult: false },
                $"Re-enabling {kind} clears its bypass result when another required child is invalid");
            fixture.Tree.SetModifierEnabled(modifier, false, out _);
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
            fixture.Replace(source, new LineCurve(Point3d.Origin, new Point3d(2, 0, 0)));
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
            Check(evaluator.Find(modifier) is { IsCurrent: false, HasResult: false },
                $"Disabled {kind} clears its preview if the first input becomes invalid");
        }

        using var one = new Fixture();
        var a = one.Add(Box(0, 10));
        var off = one.Modifier(TreeNodeKind.BooleanDifference, a);
        one.Tree.SetModifierEnabled(off, false, out _);
        using var engine = new TreeEvaluator();
        engine.Rebuild(one.Tree, one.Source, Tolerance);
        Check(Current(engine.Find(off)!, 1, 10), "A disabled Boolean accepts one input without a cutter");
        var b = one.Add(Box(20, 30));
        one.Move(b, off);
        engine.Rebuild(one.Tree, one.Source, Tolerance);
        Check(engine.InputPreviews.Count == 2 && !engine.InputPreviews[0].IsCutter && engine.InputPreviews[1].IsCutter,
            "Disabled secondary inputs retain a hidden wire/selection preview without affecting the result");

        var missing = one.Modifier(TreeNodeKind.BooleanDifference);
        one.Move(missing, off);
        engine.Rebuild(one.Tree, one.Source, Tolerance);
        Check(Current(engine.Find(off)!, 1, 10) && engine.Find(missing) is { IsCurrent: false },
            "A disabled parent ignores an incomplete secondary Modifier");
        one.Tree.SetModifierEnabled(missing, false, out _);
        engine.Rebuild(one.Tree, one.Source, Tolerance);
        Check(engine.Find(missing) is { HasResult: false, Status: "Needs inputs" }, "A disabled Modifier with no input has no preview");

        using var empty = new Fixture();
        var left = empty.Add(Box(0, 1));
        var right = empty.Add(Box(3, 4));
        var emptyChild = empty.Modifier(TreeNodeKind.BooleanIntersection, left, right);
        var ignored = empty.Add(Box(5, 6));
        var emptyOff = empty.Modifier(TreeNodeKind.Array, emptyChild, ignored);
        empty.Tree.SetModifierEnabled(emptyOff, false, out _);
        engine.Rebuild(empty.Tree, empty.Source, Tolerance);
        Check(Current(engine.Find(emptyOff)!, 0, 0), "Disabled Modifier preserves an empty first input and does not leak later geometry");
        empty.Tree.SetModifierEnabled(emptyOff, true, out _);
        empty.Tree.Move(ignored, null, empty.Tree.Roots.Count, out _);
        engine.Rebuild(empty.Tree, empty.Source, Tolerance);
        Check(Current(engine.Find(emptyOff)!, 0, 0), "Array of a valid empty result stays current and empty");
    }

    private static void NestedPatterns()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        var array = fixture.Modifier(TreeNodeKind.Array, mirror);
        fixture.Tree.SetArraySettings(array, new ArraySettings { CountX = 3, Spacing = new ModifierVector(10, 0, 0) }, out _);
        using var evaluator = new TreeEvaluator();
        evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        Check(Current(evaluator.Find(array)!, 6, 6), "Array consumes every piece of a nested Mirror result");
        var union = fixture.Modifier(TreeNodeKind.BooleanUnion, array, fixture.Add(Box(1, 2)));
        evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        Check(Current(evaluator.Find(union)!, 6, 6), "A parent Union can consolidate a pattern's overlapping copies");

        using var difference = new Fixture();
        var a = difference.Add(Box(0, 10));
        var cutter = difference.Add(Box(2, 3, -1, 2, -1, 2));
        var cutterArray = difference.Modifier(TreeNodeKind.Array, cutter);
        difference.Tree.SetArraySettings(cutterArray, new ArraySettings { CountX = 3, Spacing = new ModifierVector(3, 0, 0) }, out _);
        var result = difference.Modifier(TreeNodeKind.BooleanDifference, a, cutterArray);
        evaluator.Rebuild(difference.Tree, difference.Source, Tolerance);
        Check(Current(evaluator.Find(result)!, 4, 7) && evaluator.InputPreviews.Single(input => input.ObjectId == difference.ObjectId(cutter)).IsCutter,
            "An Array can supply every repeated cutter to a parent Difference");
    }

    private static void LivePatterns()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Box(1, 2));
        var mirror = fixture.Modifier(TreeNodeKind.Mirror, source);
        fixture.Tree.SetMirrorSettings(mirror, MirrorSettings.Default with { KeepOriginal = false }, out _);
        var distant = fixture.Add(Box(30, 31));
        var root = fixture.Modifier(TreeNodeKind.BooleanUnion, mirror, distant);
        using var committed = new TreeEvaluator();
        using var live = new LiveTreePreview();
        committed.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        var transforms = fixture.Tree.SourcesInSubtree(root).ToDictionary(id => id, _ => Transform.Translation(10, 0, 0));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, committed);
        var current = live.Find(root)!;
        Check(Current(current, 2, 2) && Near(Bounds(current).Min.X, -12) && Near(Bounds(current).Max.X, 41) &&
              live.LastReusedModifierCount == 0 && live.LastBooleanCount == 1,
            "Live translation re-evaluates a world-plane Mirror and its Boolean ancestors");
        transforms = fixture.Tree.SourcesInSubtree(root).ToDictionary(id => id, _ => Transform.Translation(20, 0, 0));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, committed);
        Check(Near(Bounds(live.Find(root)!).Min.X, -22) && Near(Bounds(committed.Find(root)!).Min.X, -2),
            "Consecutive Mirror drag frames use absolute transforms and preserve the committed preview");
        live.Update(fixture.Tree, fixture.Source, new Dictionary<Guid, Transform> { [fixture.ObjectId(distant)] = Transform.Translation(1, 0, 0) },
            Tolerance, committed);
        Check(Near(Bounds(live.Find(root)!).Min.X, -2), "Removing a live source transform restores the Mirror to its original position");
        Check(live.Clear() && live.Find(root) is null, "Cancelling a Mirror drag discards the transient result");

        using var rectangular = new Fixture();
        var input = rectangular.Add(Box(1, 2));
        var array = rectangular.Modifier(TreeNodeKind.Array, input);
        committed.Rebuild(rectangular.Tree, rectangular.Source, Tolerance);
        var move = new Dictionary<Guid, Transform> { [rectangular.ObjectId(input)] = Transform.Translation(5, 0, 0) };
        live.Update(rectangular.Tree, rectangular.Source, move, Tolerance, committed);
        Check(Current(live.Find(array)!, 2, 2) && Near(Bounds(live.Find(array)!).Min.X, 6) && Near(Bounds(live.Find(array)!).Max.X, 17) &&
              live.LastReusedModifierCount == 1 && live.LastBooleanCount == 0,
            "Rectangular Array preserves the whole-translation cache optimization");
        rectangular.Tree.SetArraySettings(array, new ArraySettings { CountX = 3, Spacing = new ModifierVector(20, 0, 0) }, out _);
        live.Update(rectangular.Tree, rectangular.Source, move, Tolerance, committed);
        Check(Current(live.Find(array)!, 3, 3) && Near(Bounds(live.Find(array)!).Max.X, 47),
            "Editing Array settings invalidates a live cache at an unchanged cursor position");
        rectangular.Tree.SetModifierEnabled(array, false, out _);
        live.Update(rectangular.Tree, rectangular.Source, move, Tolerance, committed);
        Check(Current(live.Find(array)!, 1, 1) && Near(Bounds(live.Find(array)!).Max.X, 7),
            "Disabling a live Array immediately discards its generated copies");
    }

    private static void OverlappingPatterns()
    {
        foreach (var pattern in new[] { TreeNodeKind.Array, TreeNodeKind.Mirror })
        {
            using var fixture = new Fixture();
            var input = fixture.Add(pattern == TreeNodeKind.Array ? Box(0, 10) : Box(-5, 10));
            var patternId = fixture.Modifier(pattern, input);
            if (pattern == TreeNodeKind.Array)
                fixture.Tree.SetArraySettings(patternId, new ArraySettings { CountX = 2, Spacing = new ModifierVector(5, 0, 0) }, out _);
            else fixture.Tree.SetMirrorSettings(patternId, MirrorSettings.Default with { Union = false }, out _);
            using var evaluator = new TreeEvaluator();
            evaluator.Rebuild(fixture.Tree, fixture.Source, Tolerance);
            var output = evaluator.Find(patternId)!;
            Check(Current(output, 2, pattern == TreeNodeKind.Array ? 20 : 30),
                $"Overlapping {pattern} output retains both independently editable instances");
            using var compound = ResultGeometry.Create(output.Results);
            using var enclosing = Box(-20, 30, -1, 2, -1, 2);
            using var remote = Box(40, 41);
            var expected = pattern == TreeNodeKind.Array ? 15 : 20;
            foreach (var kind in new[] { TreeNodeKind.BooleanIntersection, TreeNodeKind.BooleanDifference, TreeNodeKind.BooleanUnion })
            foreach (var materialized in new[] { false, true })
            {
                IReadOnlyList<Brep> first = materialized ? [compound] : output.Results;
                var second = kind == TreeNodeKind.BooleanIntersection ? enclosing : remote;
                var result = BooleanEvaluator.Evaluate(kind, [first, [second]], Tolerance);
                try
                {
                    Check(result.All(brep => brep.IsValid && brep.IsSolid) && Near(result.Sum(Volume), expected + (kind == TreeNodeKind.BooleanUnion ? 1 : 0)),
                        $"{kind} treats {(materialized ? "materialized" : "live")} overlapping {pattern} copies as one solid set");
                }
                finally { foreach (var brep in result) brep.Dispose(); }
            }
            using var cutBody = Box(-20, 30);
            var asCutter = BooleanEvaluator.Evaluate(TreeNodeKind.BooleanDifference, [[cutBody], output.Results], Tolerance);
            try
            {
                Check(Near(asCutter.Sum(Volume), 50 - expected),
                    $"Difference normalizes overlapping {pattern} cutter copies without subtracting overlap twice");
            }
            finally { foreach (var brep in asCutter) brep.Dispose(); }
        }
    }

    private static void Commit(TreeNodeKind kind, ResultCommitMode mode)
    {
        using var document = RhinoDoc.CreateHeadless(null);
        document.ModelAbsoluteTolerance = Tolerance;
        document.UndoRecordingEnabled = true;
        using var input = Box(1, 2);
        var objectId = document.Objects.AddBrep(input);
        var tree = new ModifierTreeModel();
        tree.RegisterSource(objectId);
        var modifier = tree.AddModifier(kind);
        tree.Move(tree.FindSource(objectId)!.Id, modifier.Id, 0, out _);
        using var data = new TreeDocumentData(document);
        data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
        var before = TreeStateCodec.Encode(data.Capture());
        Check(ResultCommit.Apply(document, data, modifier.Id, mode, out var resultId, out var error),
            $"{mode} accepts a current {kind} pattern result: {error}");
        var resultObjectId = data.Tree.Find(resultId)!.ObjectId!.Value;
        var brep = (Brep)document.Objects.FindId(resultObjectId)!.Geometry;
        Check(brep.IsValid && brep.IsSolid && Near(Volume(brep), 2),
            $"{mode} materializes all {kind} copies into one Brep object");
        Check(document.Undo() && TreeStateCodec.Encode(data.Capture()) == before && document.Objects.FindId(resultObjectId) is null,
            $"One {kind} {mode} Undo restores the original pattern and its settings");
    }

    private sealed class Fixture : IDisposable
    {
        public ModifierTreeModel Tree { get; } = new();
        private readonly Dictionary<Guid, GeometryBase> _sources = [];
        public Guid Add(GeometryBase geometry)
        {
            var id = Guid.NewGuid();
            _sources.Add(id, geometry);
            Tree.RegisterSource(id);
            return Tree.FindSource(id)!.Id;
        }
        public Guid ObjectId(Guid node) => Tree.Find(node)!.ObjectId!.Value;
        public Brep Geometry(Guid node) => (Brep)_sources[ObjectId(node)];
        public GeometryBase? Source(Guid id) => _sources.GetValueOrDefault(id);
        public void Replace(Guid node, GeometryBase geometry)
        {
            var id = ObjectId(node);
            _sources[id].Dispose();
            _sources[id] = geometry;
        }
        public Guid Modifier(TreeNodeKind kind, params Guid[] children)
        {
            var node = Tree.AddModifier(kind);
            foreach (var child in children) Move(child, node.Id);
            return node.Id;
        }
        public void Move(Guid child, Guid parent)
        {
            if (!Tree.Move(child, parent, Tree.Find(parent)!.Children.Count, out var error)) throw new Exception(error);
        }
        public void Dispose() { foreach (var source in _sources.Values) source.Dispose(); }
    }

    private static Brep Box(double x0, double x1, double y0 = 0, double y1 = 1, double z0 = 0, double z1 = 1) =>
        new BoundingBox(x0, y0, z0, x1, y1, z1).ToBrep();
    private static bool Current(NodeEvaluation result, int count, double volume) =>
        result.IsCurrent && result.Results.Count == count && result.Results.All(brep => brep.IsValid && brep.IsSolid) &&
        Near(result.Results.Sum(Volume), volume);
    private static BoundingBox Bounds(NodeEvaluation result)
    {
        var bounds = BoundingBox.Empty;
        foreach (var brep in result.Results) bounds.Union(brep.GetBoundingBox(true));
        return bounds;
    }
    private static double Volume(Brep brep)
    {
        using var properties = VolumeMassProperties.Compute(brep);
        return properties.Volume;
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
