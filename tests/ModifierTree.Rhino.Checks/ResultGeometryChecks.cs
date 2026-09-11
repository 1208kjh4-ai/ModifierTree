using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Geometry;

internal static class ResultGeometryChecks
{
    public static void Run()
    {
        using var a = Box(0, 10, 0, 10, 0, 10);
        using var slab = Box(4, 6, -1, 11, -1, 11);
        using var evaluation = new NodeEvaluation();
        evaluation.Succeed(DifferenceEvaluator.Evaluate(a, slab, 0.001));
        Check(evaluation.Results.Count == 2, "Materialization fixture has two disconnected Boolean pieces");
        using var combined = ResultGeometry.Create(evaluation);
        Check(combined.IsValid && combined.IsSolid && Math.Abs(Volume(combined) - 800) < 0.01,
            "Materialization retains both closed solids and their total volume in one Brep");
        var components = Brep.SplitDisjointPieces(combined);
        try
        {
            Check(components.Length == 2 && components.All(piece => piece.IsValid && piece.IsSolid),
                "One result Brep retains two separate valid closed components");
        }
        finally { foreach (var component in components) component.Dispose(); }
        Check(evaluation.Results.All(piece => piece.Faces.Count == 6) &&
              Math.Abs(evaluation.Results.Sum(Volume) - 800) < 0.01,
            "Materialization preserves the original preview pieces");

        // A hollow solid has multiple shells, but its internal void must not become filled.
        using var inner = Box(2, 8, 2, 8, 2, 8);
        inner.Flip();
        using var hollow = ResultGeometry.Create(new[] { a, inner });
        Check(hollow.IsValid && hollow.IsSolid && Math.Abs(Volume(hollow) - 784) < 0.01,
            "Materialization preserves the enclosed cavity of a hollow result");
        var hollowComponents = hollow.GetConnectedComponents();
        try
        {
            Check(hollowComponents.Length == 2 &&
                  hollowComponents.Any(piece => piece.SolidOrientation == BrepSolidOrientation.Inward),
                "Connected component copies preserve inward cavity shell orientation");
        }
        finally { foreach (var component in hollowComponents) component.Dispose(); }
        using (var probeCutter = Box(-1, 2, -1, 11, -1, 11))
        {
            var cutHollow = DifferenceEvaluator.Evaluate(hollow, probeCutter, 0.001);
            try { Check(Math.Abs(cutHollow.Sum(Volume) - 584) < 0.01, "Parent Difference preserves an enclosed cavity while cutting its outer solid"); }
            finally { foreach (var value in cutHollow) value.Dispose(); }
            using var remote = Box(20, 30, 0, 10, 0, 10);
            using var hollowAndRemote = ResultGeometry.Create(new[] { hollow, remote });
            var cutDisconnected = DifferenceEvaluator.Evaluate(hollowAndRemote, probeCutter, 0.001);
            try { Check(Math.Abs(cutDisconnected.Sum(Volume) - 1584) < 0.01, "Parent Difference preserves both a hollow solid and an untouched disconnected solid"); }
            finally { foreach (var value in cutDisconnected) value.Dispose(); }
        }

        using (var doc = RhinoDoc.CreateHeadless(null))
        {
            doc.ModelAbsoluteTolerance = 0.001;
            var objectId = doc.Objects.AddBrep(combined);
            Check(objectId != Guid.Empty && doc.Objects.Count == 1 &&
                  doc.Objects.FindId(objectId)?.Geometry is Brep stored &&
                  stored.IsSolid && Math.Abs(Volume(stored) - 800) < 0.01,
                "Adding a disconnected materialized result creates exactly one native Rhino object");
            using var cutter = Box(-1, 2, -1, 11, -1, 11);
            var cutterId = doc.Objects.AddBrep(cutter);
            var tree = new ModifierTreeModel();
            tree.RegisterSource(objectId);
            tree.RegisterSource(cutterId);
            var parent = tree.AddModifier();
            Move(tree, tree.FindSource(objectId)!.Id, parent.Id, 0);
            Move(tree, tree.FindSource(cutterId)!.Id, parent.Id, 1);
            using var engine = new TreeEvaluator();
            engine.Rebuild(tree, id => doc.Objects.FindId(id)?.Geometry, doc.ModelAbsoluteTolerance);
            var value = engine.Find(parent.Id)!;
            Check(value.IsCurrent && Math.Abs(value.Results.Sum(Volume) - 600) < 0.01,
                $"A parent Difference evaluates every component of a materialized primary source (volume={value.Results.Sum(Volume)}, error={value.Error})");
            Move(tree, tree.FindSource(cutterId)!.Id, parent.Id, 0);
            engine.Rebuild(tree, id => doc.Objects.FindId(id)?.Geometry, doc.ModelAbsoluteTolerance);
            value = engine.Find(parent.Id)!;
            Check(value.IsCurrent && Math.Abs(value.Results.Sum(Volume) - 232) < 0.01,
                $"A parent Difference accepts the materialized disconnected result as a cutter source (volume={value.Results.Sum(Volume)}, error={value.Error})");
        }

        using var single = ResultGeometry.Create(new[] { a });
        single.Transform(Transform.Translation(50, 0, 0));
        Check(Math.Abs(a.GetBoundingBox(true).Min.X) < 0.001 && Math.Abs(single.GetBoundingBox(true).Min.X - 50) < 0.001,
            "A single materialized result is an independent geometry copy");
        using var inward = a.DuplicateBrep();
        inward.Flip();
        using var normalized = ResultGeometry.Create(new[] { inward });
        Check(normalized.SolidOrientation == BrepSolidOrientation.Inward &&
              inward.SolidOrientation == BrepSolidOrientation.Inward,
            "Materialization preserves shell orientation without changing its source");
        evaluation.Fail("Blocked", "Input unavailable.");
        Reject(() => ResultGeometry.Create(evaluation), "Materialization rejects a stale retained preview");
        using var pending = new NodeEvaluation();
        Reject(() => ResultGeometry.Create(pending), "Materialization rejects an unevaluated Modifier");
        pending.Succeed([]);
        Reject(() => ResultGeometry.Create(pending), "Materialization rejects a current empty result without making an object");
        using var open = a.Faces[0].DuplicateFace(false);
        Reject(() => ResultGeometry.Create(new[] { open }), "Materialization rejects an open Brep");
        using var invalid = new Brep();
        Reject(() => ResultGeometry.Create(new[] { a, invalid }), "Materialization rejects any invalid result piece");
    }

    private static Brep Box(double x0, double x1, double y0, double y1, double z0, double z1) =>
        new Box(Plane.WorldXY, new Interval(x0, x1), new Interval(y0, y1), new Interval(z0, z1)).ToBrep();

    private static double Volume(Brep geometry)
    {
        using var mass = VolumeMassProperties.Compute(geometry);
        return mass.Volume;
    }

    private static void Move(ModifierTreeModel tree, Guid id, Guid parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }

    private static void Reject(Func<Brep> create, string message)
    {
        try { using var unexpected = create(); }
        catch (InvalidOperationException) { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
