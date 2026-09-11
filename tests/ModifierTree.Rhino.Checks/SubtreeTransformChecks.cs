using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Geometry;

internal static class SubtreeTransformChecks
{
    public static void Run()
    {
        // The windowless host cannot reliably start a new Undo record after Undo.
        // Use a fresh document for each scenario; perform its Undo last.
        foreach (var scenario in new[] { "root", "nested", "source", "rejected" }) Verify(scenario);
    }

    private static void Verify(string scenario)
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        doc.ModelAbsoluteTolerance = 0.001;
        using var boxA = new BoundingBox(0, 0, 0, 10, 10, 10).ToBrep();
        using var boxB = new BoundingBox(5, -1, -1, 15, 11, 11).ToBrep();
        using var boxC = new BoundingBox(0, 0, 0, 2, 10, 10).ToBrep();
        var a = doc.Objects.AddBrep(boxA); var b = doc.Objects.AddBrep(boxB); var c = doc.Objects.AddBrep(boxC);
        var tree = new ModifierTreeModel();
        foreach (var id in new[] { a, b, c }) tree.RegisterSource(id);
        var nested = tree.AddModifier(); var root = tree.AddModifier();
        tree.Move(tree.FindSource(a)!.Id, nested.Id, 0, out _);
        tree.Move(tree.FindSource(b)!.Id, nested.Id, 1, out _);
        tree.Move(nested.Id, root.Id, 0, out _);
        tree.Move(tree.FindSource(c)!.Id, root.Id, 1, out _);
        using var evaluator = new TreeEvaluator();
        void Rebuild() => evaluator.Rebuild(tree, id => doc.Objects.FindId(id)?.Geometry, doc.ModelAbsoluteTolerance);
        double Volume() => evaluator.Find(root.Id)!.Results.Sum(brep => brep.GetVolume());
        double X(Guid id) => doc.Objects.FindId(id)!.Geometry.GetBoundingBox(true).Min.X;
        bool Move(Guid nodeId, double x, out string error)
        {
            var record = doc.BeginUndoRecord("Move subtree test");
            if (record == 0) throw new Exception("Could not open test Undo record.");
            try { return SubtreeTransform.Apply(doc, tree, nodeId, Transform.Translation(x, 0, 0), out error); }
            finally { doc.EndUndoRecord(record); }
        }
        Rebuild();
        if (scenario == "root")
        {
        Check(Math.Abs(Volume() - 300) < 0.01, "Nested move fixture starts at volume 300");
        Check(Move(root.Id, 20, out _) && X(a) == 20 && X(b) == 25 && X(c) == 20,
            "Root Move applies the same translation to every source and preserves GUIDs");
        Rebuild();
        Check(Math.Abs(Volume() - 300) < 0.01 && evaluator.Find(root.Id)!.Results[0].GetBoundingBox(true).Min.X == 22,
            "Root Move preserves the Boolean shape at its new location");
        Check(doc.Undo() && X(a) == 0 && X(b) == 5 && X(c) == 0, "One Undo restores all sources from a root Move");
        Rebuild();
        Check(Math.Abs(Volume() - 300) < 0.01, "Rebuild after root Move Undo restores the result");
        return;
        }
        if (scenario == "nested")
        {
        Check(Move(nested.Id, 20, out _) && X(a) == 20 && X(b) == 25 && X(c) == 0,
            "Nested Move leaves its sibling cutter unchanged");
        Rebuild();
        Check(Math.Abs(Volume() - 500) < 0.01, "Parent re-evaluates the moved subtree against its unmoved cutter");
        Check(doc.Undo() && X(a) == 0 && X(b) == 5 && X(c) == 0, "One Undo restores a nested Move");
        return;
        }
        if (scenario == "source")
        {
        Check(Move(tree.FindSource(b)!.Id, 30, out _) && X(a) == 0 && X(b) == 35 && X(c) == 0,
            "Geometry Move changes only the selected source");
        Rebuild();
        Check(Math.Abs(Volume() - 800) < 0.01, "Individual cutter Move changes the final subtraction");
        Check(doc.Undo(), "Individual Move can be undone");
        return;
        }
        doc.Objects.Lock(b, true);
        Check(!Move(root.Id, 10, out _) && X(a) == 0 && X(b) == 5 && X(c) == 0,
            "Locked descendant rejects the whole Move without changing siblings");
        doc.Objects.Unlock(b, true);
        doc.Objects.Hide(b, true);
        Check(!Move(root.Id, 10, out _) && X(a) == 0 && X(c) == 0,
            "Rhino-hidden descendant rejects Move before any writes");
        doc.Objects.Show(b, true);
        doc.Objects.Delete(b, true);
        Check(!Move(root.Id, 10, out _) && X(a) == 0 && X(c) == 0,
            "Missing descendant rejects Move before any writes");
        Check(!SubtreeTransform.Validate(doc, tree, tree.AddModifier().Id, out _), "Empty Modifier cannot start Move");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
