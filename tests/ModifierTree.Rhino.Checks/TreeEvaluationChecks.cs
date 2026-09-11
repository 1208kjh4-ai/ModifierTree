using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class TreeEvaluationChecks
{
    public static void Run()
    {
        var tree = new ModifierTreeModel();
        using var engine = new TreeEvaluator();
        using var a = Box(0, 10, 0, 10, 0, 10);
        using var b = Box(5, 15, -1, 11, -1, 11);
        using var c = Box(-1, 2, -1, 11, -1, 11);
        using var disjoint = Box(20, 30, 20, 30, 20, 30);
        using var invalid = new LineCurve(Point3d.Origin, new Point3d(1, 0, 0));
        var sources = new Dictionary<Guid, GeometryBase?>();
        Guid Register(GeometryBase geometry)
        {
            var id = Guid.NewGuid();
            sources.Add(id, geometry);
            tree.RegisterSource(id);
            return tree.FindSource(id)!.Id;
        }
        var aNode = Register(a);
        var bNode = Register(b);
        var cNode = Register(c);
        var modifier = tree.AddModifier();
        void Rebuild() => engine.Rebuild(tree, id => sources.GetValueOrDefault(id), 0.001);
        void Move(Guid node, Guid? parent, int index)
        {
            if (!tree.Move(node, parent, index, out var error)) throw new Exception(error);
            Rebuild();
        }
        Rebuild();
        Check(engine.Find(modifier.Id)?.Status == "Needs inputs" && !engine.Find(modifier.Id)!.HasResult, "Empty Modifier waits without generating geometry");
        Move(aNode, modifier.Id, 0);
        Check(engine.Find(modifier.Id)?.Status == "Needs inputs", "One input waits for a cutter");
        Move(bNode, modifier.Id, 1);
        Volume(engine.Find(modifier.Id)!, 500, "Dragging two sources into Modifier evaluates tree order");
        Move(bNode, modifier.Id, 0);
        Volume(engine.Find(modifier.Id)!, 940, "Reordering children changes A-B to B-A");
        Move(aNode, modifier.Id, 0);
        Move(cNode, modifier.Id, 2);
        Volume(engine.Find(modifier.Id)!, 300, "Third and later children act as additional cutters");
        Move(cNode, null, tree.Roots.Count);
        Volume(engine.Find(modifier.Id)!, 500, "Dragging a cutter out updates the old Modifier");
        var parent = tree.AddModifier();
        Move(modifier.Id, parent.Id, 0);
        Move(cNode, parent.Id, 1);
        Volume(engine.Find(parent.Id)!, 300, "A nested Modifier supplies its computed result to its parent");
        Check(engine.RootResults.Count == 1 && engine.InputPreviews.Count == 3, "Only the root result is displayed while its sources are collected once");

        var bId = tree.Find(bNode)!.ObjectId!.Value;
        sources[bId] = disjoint;
        Rebuild();
        Volume(engine.Find(parent.Id)!, 800, "Source edits propagate through nested Modifiers");
        sources[bId] = invalid;
        Rebuild();
        Check(!engine.Find(modifier.Id)!.IsCurrent && !engine.Find(parent.Id)!.IsCurrent && engine.Find(parent.Id)!.HasResult,
            "A failed descendant blocks the parent and keeps its last valid result");
        Check(engine.InputPreviews.Count == 0, "Failed root leaves original document sources visible");
        sources[bId] = b;
        Rebuild();
        Volume(engine.Find(parent.Id)!, 300, "Fixing a source recovers the nested result");

        // An empty expression change must not reuse a result from the previous arrangement.
        Move(cNode, null, tree.Roots.Count);
        Check(!engine.Find(parent.Id)!.HasResult && engine.Find(parent.Id)!.Status == "Needs inputs", "Removing a required input clears obsolete result geometry");
        tree.Remove(parent.Id);
        Rebuild();
        Volume(engine.Find(modifier.Id)!, 500, "Removing a parent promotes and displays the surviving subtree");

        using var slab = Box(4, 6, -1, 11, -1, 11);
        sources[bId] = slab;
        Rebuild();
        Check(engine.Find(modifier.Id)!.Results.Count == 2, "Intermediate output can contain multiple pieces");
        parent = tree.AddModifier();
        Move(modifier.Id, parent.Id, 0);
        Move(cNode, parent.Id, 1);
        Volume(engine.Find(parent.Id)!, 600, "Parent receives all pieces of a child result");

        using var coversA = Box(-1, 11, -1, 11, -1, 11);
        sources[bId] = coversA;
        Rebuild();
        Check(engine.Find(parent.Id)!.IsCurrent && engine.Find(parent.Id)!.Results.Count == 0,
            "Empty primary child propagates a valid empty result");
        Move(cNode, parent.Id, 0);
        Volume(engine.Find(parent.Id)!, 432, "Empty cutter child subtracts nothing from the primary input");
    }

    private static Brep Box(double x0, double x1, double y0, double y1, double z0, double z1) =>
        new Box(Plane.WorldXY, new Interval(x0, x1), new Interval(y0, y1), new Interval(z0, z1)).ToBrep();

    private static void Volume(NodeEvaluation value, double expected, string message)
    {
        double volume = 0;
        foreach (var geometry in value.Results)
        {
            using var mass = VolumeMassProperties.Compute(geometry);
            volume += mass.Volume;
        }
        Check(value.IsCurrent && Math.Abs(volume - expected) < 0.01, $"{message}: expected {expected}, actual {volume}, error={value.Error}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
