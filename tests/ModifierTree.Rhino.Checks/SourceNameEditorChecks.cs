using System.Drawing;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class SourceNameEditorChecks
{
    public static void Run()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        using var box = new BoundingBox(0, 0, 0, 10, 10, 10).ToBrep();
        using var attributes = new ObjectAttributes { Name = "Cutter", ObjectColor = Color.Red, ColorSource = ObjectColorSource.ColorFromObject };
        var id = doc.Objects.AddBrep(box, attributes);
        var tree = new ModifierTreeModel();
        tree.RegisterSource(id);
        var nodeId = tree.FindSource(id)!.Id;
        var revision = tree.Revision;
        Check(SourceNameEditor.Rename(doc, id, "절삭 객체 01", out _), "Source name supports Unicode inline edits");
        var obj = doc.Objects.FindId(id)!;
        Check(obj.Attributes.Name == "절삭 객체 01" && tree.FindSource(id)!.Id == nodeId && tree.Revision == revision,
            "Renaming updates Rhino's object name without changing tree identity or structure");
        Check(doc.Objects.Count == 1 && obj.Attributes.ObjectColor.ToArgb() == Color.Red.ToArgb() && obj.Geometry.GetBoundingBox(true).Equals(box.GetBoundingBox(true)),
            "Renaming preserves object count, color and geometry");
        doc.Modified = false;
        Check(SourceNameEditor.Rename(doc, id, "절삭 객체 01", out _) && !doc.Modified, "Unchanged names produce no document edit");
        Check(doc.Undo() && doc.Objects.FindId(id)!.Attributes.Name == "Cutter", "One Undo restores the previous Rhino object name");

        using var rejectedDoc = RhinoDoc.CreateHeadless(null);
        var lockedId = rejectedDoc.Objects.AddBrep(box, attributes);
        rejectedDoc.Objects.Lock(lockedId, true);
        Check(!SourceNameEditor.Rename(rejectedDoc, lockedId, "Changed", out _) && rejectedDoc.Objects.FindId(lockedId)!.Attributes.Name == "Cutter",
            "A locked source rejects rename without changing its name");
        Check(!SourceNameEditor.Rename(rejectedDoc, Guid.NewGuid(), "Changed", out _), "A missing source rejects rename");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
