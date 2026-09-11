using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.Geometry;

internal static class TreeUndoChecks
{
    private static bool _multipleUndo;
    public static void Run()
    {
        _multipleUndo = BaselineMultipleUndo();
        foreach (var scenario in new[] { "register", "add", "move", "remove", "visibility", "mixed", "record", "noop", "readonly", "disposed" }) Verify(scenario);
    }

    private static bool BaselineMultipleUndo()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        var id = doc.Objects.AddPoint(Point3d.Origin);
        for (var i = 0; i < 2; i++)
        {
            var record = doc.BeginUndoRecord("Baseline move without Modifier Tree");
            doc.Objects.Transform(id, Transform.Translation(1, 0, 0), true);
            doc.EndUndoRecord(record);
        }
        var first = doc.Undo(); var second = doc.Undo();
        Console.WriteLine($"HOST BASELINE repeated Undo (no tree): first={first}, second={second}, UndoActive={doc.UndoActive}, RedoActive={doc.RedoActive}");
        return first && second;
    }

    private static void Verify(string scenario)
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        using var aShape = new BoundingBox(0, 0, 0, 10, 10, 10).ToBrep();
        using var bShape = new BoundingBox(5, -1, -1, 15, 11, 11).ToBrep();
        var a = doc.Objects.AddBrep(aShape); var b = doc.Objects.AddBrep(bShape);
        using var data = new TreeDocumentData(doc);
        var fixture = new ModifierTreeModel();
        fixture.RegisterSource(a); fixture.RegisterSource(b);
        var modifier = fixture.AddModifier();
        fixture.Move(fixture.FindSource(a)!.Id, modifier.Id, 0, out _);
        fixture.Move(fixture.FindSource(b)!.Id, modifier.Id, 1, out _);
        var state = TreeStateCodec.Capture(fixture, new InputVisibility(), true);
        var serial = doc.NextUndoRecordSerialNumber;
        doc.Modified = false;
        if (scenario != "register") data.Load(state);
        var before = TreeStateCodec.Encode(data.Capture());
        if (scenario == "register")
        {
            Check(data.Edit("Register two sources", (tree, _) => { tree.RegisterSource(a); tree.RegisterSource(b); return true; }, out _),
                "One batch registration creates a custom-data-only Rhino Undo record");
            Check(doc.Modified && data.Tree.SourceCount == 2 && doc.Objects.Count == 2,
                "Tree edits mark the file modified without creating preview objects");
            Check(doc.Undo() && data.Tree.SourceCount == 0 && doc.Objects.FindId(a) is not null && doc.Objects.FindId(b) is not null,
                "One native Undo unregisters the entire batch while retaining original geometry");
            return;
        }
        if (scenario == "noop")
        {
            Check(!doc.Modified && serial == doc.NextUndoRecordSerialNumber && TreeStateCodec.Encode(data.Capture()) == TreeStateCodec.Encode(state),
                "Loading a tree restores IDs and settings without dirtying the document or creating Undo");
            Check(!data.Edit("Duplicate registration", (tree, _) => tree.RegisterSource(a), out _) &&
                  !data.Edit("Same position", (tree, visibility) => tree.Move(fixture.FindSource(a)!.Id, modifier.Id, 0, out _), out _) &&
                  !doc.Modified && serial == doc.NextUndoRecordSerialNumber,
                "Duplicate registration and no-op drag add no Undo record or unsaved change");
            return;
        }
        if (scenario == "readonly")
        {
            data.ReadOnlyReason = "Unsupported saved tree schema";
            Check(!data.Edit("Protected edit", (tree, _) => { tree.AddModifier(); return true; }, out var error) &&
                  error.Length > 0 && TreeStateCodec.Encode(data.Capture()) == before && !doc.Modified,
                "An unreadable saved tree cannot be overwritten by new tree edits");
            return;
        }
        if (scenario == "add")
        {
            var newId = Guid.Empty;
            data.Edit("Add Modifier", (tree, _) => { newId = tree.AddModifier().Id; return true; }, out _);
            var after = TreeStateCodec.Encode(data.Capture());
            Check(doc.Undo() && TreeStateCodec.Encode(data.Capture()) == before, "Native Undo removes only the newly added Modifier");
            if (doc.Redo())
            {
                Check(TreeStateCodec.Encode(data.Capture()) == after && data.Tree.Find(newId) is not null, "Native Redo restores the same Modifier ID");
                Check(doc.Undo() && TreeStateCodec.Encode(data.Capture()) == before, "Undo after Redo restores the inverse custom snapshot");
            }
            else Console.WriteLine("SKIP: Custom tree Redo requires interactive Rhino; direct Redo is unavailable in this windowless host.");
            return;
        }
        if (scenario == "remove")
        {
            data.Edit("Remove Modifier", (tree, _) => tree.Remove(modifier.Id), out _);
            Check(data.Tree.Roots.Count == 2 && doc.Undo() && TreeStateCodec.Encode(data.Capture()) == before,
                "Undo restores a removed Modifier with its exact children and original IDs");
            return;
        }
        if (scenario == "visibility")
        {
            var revision = data.Tree.Revision;
            data.Edit("Display settings", (_, wires) => { wires.Set(b, true); return true; }, out _, false);
            Check(!data.PreviewEnabled && data.InputVisibility.IsVisible(b) && doc.Undo() && data.PreviewEnabled && !data.InputVisibility.IsVisible(b),
                "Native Undo restores both result and input-wire display settings");
            Check(data.Tree.Revision == revision, "Display settings and their Undo preserve the Boolean cache revision");
            return;
        }
        if (scenario == "record")
        {
            var active = doc.BeginUndoRecord("Existing Rhino command");
            Check(active != 0 && data.Edit("Tree edit within command", (tree, _) => tree.Remove(modifier.Id), out _) &&
                  doc.UndoRecordingIsActive && doc.CurrentUndoRecordSerialNumber == active,
                "Tree edit joins an existing command Undo record without closing it");
            doc.EndUndoRecord(active);
            Check(doc.Undo() && TreeStateCodec.Encode(data.Capture()) == before, "The containing command undoes its tree edit");
            return;
        }
        if (scenario == "disposed")
        {
            data.Edit("Add before disposal", (tree, _) => { tree.AddModifier(); return true; }, out _);
            var after = TreeStateCodec.Encode(data.Capture());
            data.Dispose();
            Check(doc.Undo() && TreeStateCodec.Encode(data.Capture()) == after,
                "A retained native Undo callback does not resurrect disposed session data");
            return;
        }
        data.Edit("Reorder inputs", (tree, visibility) => tree.Move(tree.FindSource(b)!.Id, modifier.Id, 0, out _), out _);
        var reordered = TreeStateCodec.Encode(data.Capture());
        if (scenario == "mixed")
        {
            var move = doc.BeginUndoRecord("Native source movement");
            doc.Objects.Transform(b, Transform.Translation(20, 0, 0), true);
            doc.EndUndoRecord(move);
            Check(doc.Undo() && doc.Objects.FindId(b)!.Geometry.GetBoundingBox(true).Min.X == 5 && TreeStateCodec.Encode(data.Capture()) == reordered,
                "Undo first restores native movement without changing the preceding tree edit");
            if (!_multipleUndo)
            {
                Console.WriteLine("SKIP: Multiple successive Undo needs interactive Rhino; the direct API also fails in the baseline without Modifier Tree.");
                return;
            }
        }
        var undone = doc.Undo();
        Check(undone && TreeStateCodec.Encode(data.Capture()) == before,
            scenario == "mixed" ? "The next Undo restores the preceding tree edit in native history order" : "Undo restores exact input ordering after a drag edit");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
