using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Geometry;

internal static class LiveTreePreviewChecks
{
    public static void Run()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        using var aGeometry = new BoundingBox(0, 0, 0, 10, 10, 10).ToBrep();
        using var bGeometry = new BoundingBox(5, -1, -1, 20, 11, 11).ToBrep();
        using var cGeometry = new BoundingBox(0, 0, 0, 2, 10, 10).ToBrep();
        var a = doc.Objects.AddBrep(aGeometry); var b = doc.Objects.AddBrep(bGeometry); var c = doc.Objects.AddBrep(cGeometry);
        var tree = new ModifierTreeModel();
        foreach (var id in new[] { a, b, c }) tree.RegisterSource(id);
        var child = tree.AddModifier(); var root = tree.AddModifier();
        tree.Move(tree.FindSource(a)!.Id, child.Id, 0, out _);
        tree.Move(tree.FindSource(b)!.Id, child.Id, 1, out _);
        tree.Move(child.Id, root.Id, 0, out _);
        tree.Move(tree.FindSource(c)!.Id, root.Id, 1, out _);
        GeometryBase? Source(Guid id) => doc.Objects.FindId(id)?.Geometry;
        using var committed = new TreeEvaluator();
        committed.Rebuild(tree, Source, 0.001);
        using var live = new LiveTreePreview();
        bool MoveB(double x) => live.Update(tree, Source, new Dictionary<Guid, Transform> { [b] = Transform.Translation(x, 0, 0) }, 0.001);
        double Volume() => live.Find(root.Id)!.Results.Sum(brep => brep.GetVolume());
        var serials = new[] { a, b, c }.Select(id => doc.Objects.FindId(id)!.RuntimeSerialNumber).ToArray();
        var undoSerial = doc.NextUndoRecordSerialNumber;
        doc.Modified = false;
        Check(MoveB(2) && Math.Abs(Volume() - 500) < 0.01, "Moving cutter preview re-evaluates nested Boolean at the temporary position");
        Check(MoveB(3) && Math.Abs(Volume() - 600) < 0.01, "Drag transforms are absolute, not accumulated across frames");
        var count = live.RebuildCount;
        Check(!MoveB(3) && live.RebuildCount == count, "Unchanged cursor position skips redundant Boolean calculations");
        Check(MoveB(-6) && live.Find(root.Id)!.IsCurrent && live.Find(root.Id)!.Results.Count == 0,
            "A fully subtracted live result is empty without keeping obsolete geometry");
        Check(MoveB(30) && Math.Abs(Volume() - 800) < 0.01, "Moving back out recovers from an empty live result");
        using var invalid = new LineCurve(Point3d.Origin, new Point3d(1, 0, 0));
        live.Update(tree, id => id == b ? invalid : Source(id), new Dictionary<Guid, Transform> { [b] = Transform.Translation(31, 0, 0) }, 0.001);
        Check(!live.Find(root.Id)!.IsCurrent && Math.Abs(Volume() - 800) < 0.01,
            "A failed live evaluation keeps the previous valid preview");
        Check(MoveB(2) && live.Find(root.Id)!.IsCurrent && Math.Abs(Volume() - 500) < 0.01, "A valid drag position recovers after live evaluation failure");
        var wholeTree = tree.SourcesInSubtree(root.Id).ToDictionary(id => id, _ => Transform.Translation(20, 0, 0));
        live.Update(tree, Source, wholeTree, 0.001);
        Check(Math.Abs(Volume() - 300) < 0.01 && Math.Abs(live.Find(root.Id)!.Results[0].GetBoundingBox(true).Min.X - 22) < 0.001,
            "Whole-tree live movement preserves shape and relative input positions");
        Check(!doc.Modified && doc.Objects.Count == 3 && doc.NextUndoRecordSerialNumber == undoSerial &&
            serials.SequenceEqual(new[] { a, b, c }.Select(id => doc.Objects.FindId(id)!.RuntimeSerialNumber)),
            "Live previews do not modify document objects or Undo records");
        Check(live.Clear() && !live.IsActive && live.Find(root.Id) is null && Math.Abs(committed.Find(root.Id)!.Results.Sum(brep => brep.GetVolume()) - 300) < 0.01,
            "Cancellation drops transient results and preserves the committed result");
        Check(MoveB(2) && Math.Abs(Volume() - 500) < 0.01, "A new drag starts cleanly after cancellation");
        Check(live.Update(tree, Source, new Dictionary<Guid, Transform>(), 0.001) && !live.IsActive, "Ending native dynamic transforms clears the live preview");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
