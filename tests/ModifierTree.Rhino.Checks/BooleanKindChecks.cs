using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.Geometry;

internal static class BooleanKindChecks
{
    private const double Tolerance = 0.001;

    public static void Run()
    {
        SolidSets();
        CompoundInputs();
        foreach (var kind in new[] { TreeNodeKind.BooleanUnion, TreeNodeKind.BooleanIntersection })
        {
            NestedAndLive(kind);
            foreach (var mode in new[] { ResultCommitMode.Bake, ResultCommitMode.Merge }) Commit(kind, mode);
        }
    }

    private static void SolidSets()
    {
        using var a = Box(0, 10);
        using var b = Box(5, 15);
        using var c = Box(7, 12);
        using var remote = Box(20, 30);
        Result(TreeNodeKind.BooleanUnion, [[a], [b]], 1500, "Union combines overlapping solids", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [b]], 500, "Intersection keeps the common solid", 1);
        Result(TreeNodeKind.BooleanUnion, [[a], [b], [remote]], 2500, "Union accepts every child and retains a disjoint solid", 2);
        Result(TreeNodeKind.BooleanIntersection, [[a], [b], [c]], 300, "Intersection uses all three children rather than subtracting later inputs", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [remote]], 0, "Disjoint Intersection is a current empty result", 0);
        Result(TreeNodeKind.BooleanUnion, [[a], [], [b]], 1500, "Union ignores an empty child result", 1);
        Result(TreeNodeKind.BooleanUnion, [[], []], 0, "Union of empty children stays empty", 0);
        Result(TreeNodeKind.BooleanIntersection, [[a], [], [b]], 0, "Any empty Intersection child makes the whole result empty", 0);
        Result(TreeNodeKind.BooleanDifference, [[a], [b]], 500, "Boolean dispatch preserves Difference semantics", 1);
        using var duplicate = a.DuplicateBrep();
        using var contained = Box(2, 8, 2, 8, 2, 8);
        using var touching = Box(10, 20);
        Result(TreeNodeKind.BooleanUnion, [[a], [duplicate]], 1000, "Union keeps one copy of identical solids", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [duplicate]], 1000, "Intersection retains identical solids", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [contained]], 216, "Intersection retains a completely enclosed solid", 1);
        Result(TreeNodeKind.BooleanIntersection, [[contained], [a]], 216, "Enclosed Intersection is independent of child order", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [touching]], 0, "Face-touching solids have an empty solid Intersection", 0);

        a.Flip();
        Result(TreeNodeKind.BooleanUnion, [[a], [b]], 1500, "Union normalizes inward input copies", 1);
        Result(TreeNodeKind.BooleanIntersection, [[a], [b]], 500, "Intersection normalizes inward input copies", 1);
        Check(a.SolidOrientation == BrepSolidOrientation.Inward && b.SolidOrientation == BrepSolidOrientation.Outward &&
              Near(a.GetBoundingBox(true).Min.X, 0) && Near(b.GetBoundingBox(true).Min.X, 5),
            "Union and Intersection preserve their source orientation and position");
        using var open = a.Faces[0].DuplicateFace(false);
        Reject(() => BooleanEvaluator.Evaluate(TreeNodeKind.BooleanUnion, [[open], [b]], Tolerance),
            "Union rejects an open input before calling native geometry");
        Reject(() => BooleanEvaluator.Evaluate(TreeNodeKind.BooleanIntersection, [[a], [b]], double.NaN),
            "Intersection rejects a nonfinite tolerance");
        Reject(() => BooleanEvaluator.Evaluate(TreeNodeKind.Geometry, [[a], [b]], Tolerance),
            "Boolean dispatch rejects a geometry node as an operation");
    }

    private static void CompoundInputs()
    {
        using var left = Box(0, 4);
        using var right = Box(6, 10);
        using var compound = ResultGeometry.Create(new[] { left, right });
        using var bridge = Box(3, 7);
        using var clip = Box(2, 8, -1, 11, -1, 11);
        Result(TreeNodeKind.BooleanUnion, [[compound], [bridge]], 1000,
            "Union evaluates every disconnected component of a materialized source", 1);
        Result(TreeNodeKind.BooleanIntersection, [[compound], [clip]], 400,
            "Intersection treats disconnected components within one child as a solid set", 2);
        Result(TreeNodeKind.BooleanIntersection, [[compound], [clip], [bridge]], 200,
            "Three-child Intersection retains both remaining pieces of a compound child", 2);
        Check(Near(Volume(compound), 800) && compound.IsValid && compound.IsSolid,
            "Boolean evaluation leaves a materialized disconnected source intact");

        using var outer = Box(0, 10);
        using var inner = Box(2, 8, 2, 8, 2, 8);
        inner.Flip();
        using var hollow = ResultGeometry.Create(new[] { outer, inner });
        using var remote = Box(20, 30);
        using var half = Box(-1, 5, -1, 11, -1, 11);
        Result(TreeNodeKind.BooleanUnion, [[hollow], [remote]], 1784,
            "Union preserves a cavity together with an untouched distant solid", 2);
        Result(TreeNodeKind.BooleanIntersection, [[hollow], [half]], 392,
            "Intersection preserves the remaining cavity volume when clipping a hollow solid", 1);
        Result(TreeNodeKind.BooleanIntersection, [[half], [hollow]], 392,
            "Clipping a hollow solid gives the same Intersection in reversed child order", 1);
        using var inMaterial = Box(0.5, 1.5, 0.5, 1.5, 0.5, 1.5);
        using var inVoid = Box(3, 4, 3, 4, 3, 4);
        using var crossingVoid = Box(1, 3, 1, 3, 1, 3);
        using var enclosing = Box(-1, 11, -1, 11, -1, 11);
        using var shiftedHollow = hollow.DuplicateBrep();
        shiftedHollow.Transform(Transform.Translation(2, 0, 0));
        using var duplicateHollow = hollow.DuplicateBrep();
        using var exactVoid = Box(2, 8, 2, 8, 2, 8);
        Result(TreeNodeKind.BooleanUnion, [[hollow], [duplicateHollow]], 784,
            "Union retains one hollow solid when two inputs have identical geometry", 1);
        foreach (var (other, volume, label) in new[]
        {
            (inMaterial, 1d, "a solid fully inside its material"),
            (inVoid, 0d, "a solid fully inside its void"),
            (exactVoid, 0d, "a solid exactly matching its cavity boundary"),
            (crossingVoid, 7d, "a solid crossing its internal cavity"),
            (enclosing, 784d, "a solid enclosing the entire hollow input"),
            (remote, 0d, "a disjoint solid"),
            (duplicateHollow, 784d, "an identical hollow input"),
            (shiftedHollow, 512d, "another hollow solid with overlapping cavities")
        })
        {
            Result(TreeNodeKind.BooleanIntersection, [[hollow], [other]], volume,
                $"Hollow Intersection handles {label}", volume == 0 ? 0 : 1);
            Result(TreeNodeKind.BooleanIntersection, [[other], [hollow]], volume,
                $"Reversed hollow Intersection handles {label}", volume == 0 ? 0 : 1);
        }
        var cavityUnion = BooleanEvaluator.Evaluate(TreeNodeKind.BooleanUnion, [[hollow], [remote]], Tolerance);
        try
        {
            using var trim = Box(-1, 3, -1, 11, -1, 11);
            Result(TreeNodeKind.BooleanDifference, [cavityUnion, [trim]], 1520,
                "A parent Difference keeps the cavity from its Union child instead of filling its detached shell", 2);
            Result(TreeNodeKind.BooleanIntersection, [cavityUnion, [half]], 392,
                "A parent Intersection consumes a hollow Union result with its cavity attached", 1);
            using var materialized = ResultGeometry.Create(cavityUnion);
            Result(TreeNodeKind.BooleanIntersection, [[materialized], [half]], 392,
                "A materialized hollow Union result retains the same cavity when reused by a parent", 1);
        }
        finally { foreach (var result in cavityUnion) result.Dispose(); }
        Check(Near(Volume(hollow), 784) && inner.SolidOrientation == BrepSolidOrientation.Inward,
            "Shared solid decomposition preserves the original cavity orientation");
    }

    private static void NestedAndLive(TreeNodeKind kind)
    {
        using var fixture = new NestedFixture(kind);
        using var committed = new TreeEvaluator();
        committed.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        var expected = kind == TreeNodeKind.BooleanUnion ? 800 : 200;
        Evaluation(committed.Find(fixture.Parent)!, expected, $"{kind} accepts two nested Difference results");
        Check(committed.InputPreviews.Count == 4 && committed.InputPreviews.Where(input => input.IsCutter)
                  .Select(input => input.ObjectId).ToHashSet().SetEquals(new[] { fixture.B, fixture.D }),
            $"{kind} later inputs remain normal inputs while nested Difference cutters keep their role");

        using var live = new LiveTreePreview();
        var transforms = fixture.Tree.SourcesInSubtree(fixture.Parent)
            .ToDictionary(id => id, _ => Transform.Translation(20, 0, 0));
        live.Update(fixture.Tree, fixture.Source, transforms, Tolerance, committed);
        var translated = live.Find(fixture.Parent)!;
        Check(translated.IsCurrent && Near(translated.Results.Sum(Volume), expected) &&
              Near(translated.Results.Min(result => result.GetBoundingBox(true).Min.X),
                  kind == TreeNodeKind.BooleanUnion ? 20 : 23) &&
              live.LastBooleanCount == 0 && live.LastReusedModifierCount == 3,
            $"Whole nested {kind} translation reuses all three cached modifiers without Boolean work");
        live.Update(fixture.Tree, fixture.Source,
            new Dictionary<Guid, Transform> { [fixture.A] = Transform.Translation(3, 0, 0) }, Tolerance, committed);
        Check(live.LastBooleanCount == 2 && Near(live.Find(fixture.Parent)!.Results.Sum(Volume),
                  kind == TreeNodeKind.BooleanUnion ? 500 : 200) &&
              Near(committed.Find(fixture.Parent)!.Results.Sum(Volume), expected),
            $"Editing one nested {kind} input recomputes its two ancestors without changing committed geometry");

        var sourceId = fixture.Register(Box(-2, 10));
        var outerDifference = fixture.Tree.AddModifier();
        Move(fixture.Tree, fixture.Tree.FindSource(sourceId)!.Id, outerDifference.Id);
        Move(fixture.Tree, fixture.Parent, outerDifference.Id);
        committed.Rebuild(fixture.Tree, fixture.Source, Tolerance);
        Evaluation(committed.Find(outerDifference.Id)!, 1200 - expected,
            $"A nested {kind} result can be used as a Difference cutter");
        Check(committed.InputPreviews.Where(input => input.ObjectId != sourceId).All(input => input.IsCutter),
            $"Ancestor Difference cutter status propagates through every {kind} child");
    }

    private static void Commit(TreeNodeKind kind, ResultCommitMode mode)
    {
        using var document = RhinoDoc.CreateHeadless(null);
        document.ModelAbsoluteTolerance = Tolerance;
        document.UndoRecordingEnabled = true;
        using var a = Box(0, 10);
        using var b = Box(5, 15);
        var aId = document.Objects.AddBrep(a);
        var bId = document.Objects.AddBrep(b);
        var tree = new ModifierTreeModel();
        tree.RegisterSource(aId);
        tree.RegisterSource(bId);
        var modifier = tree.AddModifier(kind);
        tree.RenameModifier(modifier.Id, "Solid result", out _);
        Move(tree, tree.FindSource(aId)!.Id, modifier.Id);
        Move(tree, tree.FindSource(bId)!.Id, modifier.Id);
        using var data = new TreeDocumentData(document);
        data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
        var before = TreeStateCodec.Encode(data.Capture());
        Check(ResultCommit.Apply(document, data, modifier.Id, mode, out var resultNodeId, out var error),
            $"{mode} accepts a current {kind} result: {error}");
        var resultNode = data.Tree.Find(resultNodeId)!;
        var resultObjectId = resultNode.ObjectId!.Value;
        var result = document.Objects.FindId(resultObjectId)!;
        Check(result.Geometry is Brep brep && brep.IsValid && brep.IsSolid &&
              Near(Volume(brep), kind == TreeNodeKind.BooleanUnion ? 1500 : 500) &&
              result.Attributes.Name == "Solid result" &&
              (mode == ResultCommitMode.Bake ? data.Tree.Find(modifier.Id) is not null : data.Tree.Find(modifier.Id) is null),
            $"{mode} materializes the correct {kind} volume and preserves its naming and tree behavior");
        Check(document.Undo() && TreeStateCodec.Encode(data.Capture()) == before &&
              document.Objects.FindId(resultObjectId) is null && document.Objects.FindId(aId) is { IsHidden: false } &&
              document.Objects.FindId(bId) is { IsHidden: false },
            $"One {kind} {mode} Undo restores both the tree and native objects");
    }

    private sealed class NestedFixture : IDisposable
    {
        public ModifierTreeModel Tree { get; } = new();
        private readonly Dictionary<Guid, Brep> _sources = [];
        public Guid A { get; }
        public Guid B { get; }
        public Guid D { get; }
        public Guid Parent { get; }

        public NestedFixture(TreeNodeKind kind)
        {
            A = Register(Box(0, 10));
            B = Register(Box(5, 15, -1, 11, -1, 11));
            var c = Register(Box(3, 13));
            D = Register(Box(8, 18, -1, 11, -1, 11));
            var main = Tree.AddModifier();
            var sub = Tree.AddModifier();
            Move(Tree, Tree.FindSource(A)!.Id, main.Id);
            Move(Tree, Tree.FindSource(B)!.Id, main.Id);
            Move(Tree, Tree.FindSource(c)!.Id, sub.Id);
            Move(Tree, Tree.FindSource(D)!.Id, sub.Id);
            Parent = Tree.AddModifier(kind).Id;
            Move(Tree, main.Id, Parent);
            Move(Tree, sub.Id, Parent);
        }

        public Guid Register(Brep geometry)
        {
            var id = Guid.NewGuid();
            _sources.Add(id, geometry);
            Tree.RegisterSource(id);
            return id;
        }

        public GeometryBase? Source(Guid id) => _sources.GetValueOrDefault(id);
        public void Dispose() { foreach (var source in _sources.Values) source.Dispose(); }
    }

    private static Brep Box(double x0, double x1, double y0 = 0, double y1 = 10, double z0 = 0, double z1 = 10) =>
        new BoundingBox(x0, y0, z0, x1, y1, z1).ToBrep();

    private static void Move(ModifierTreeModel tree, Guid id, Guid parent)
    {
        if (!tree.Move(id, parent, tree.Find(parent)!.Children.Count, out var error)) throw new Exception(error);
    }

    private static void Result(TreeNodeKind kind, IReadOnlyList<IReadOnlyList<Brep>> children,
        double expected, string message, int count)
    {
        var results = BooleanEvaluator.Evaluate(kind, children, Tolerance);
        try
        {
            var volume = results.Sum(Volume);
            Check(results.Length == count && results.All(result => result.IsValid && result.IsSolid) && Near(volume, expected),
                $"{message} (expected {expected}/{count} pieces, got {volume}/{results.Length})");
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    private static void Evaluation(NodeEvaluation evaluation, double expected, string message) =>
        Check(evaluation.IsCurrent && Near(evaluation.Results.Sum(Volume), expected),
            $"{message} (expected {expected}, got {evaluation.Results.Sum(Volume)}, error={evaluation.Error})");

    private static double Volume(Brep brep)
    {
        using var properties = VolumeMassProperties.Compute(brep);
        return properties.Volume;
    }

    private static bool Near(double first, double second) => Math.Abs(first - second) < 0.01;

    private static void Reject(Func<Brep[]> action, string message)
    {
        try
        {
            var results = action();
            foreach (var result in results) result.Dispose();
        }
        catch (InvalidOperationException) { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
