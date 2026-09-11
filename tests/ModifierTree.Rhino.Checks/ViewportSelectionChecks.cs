using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Geometry;
using Rhino.Input.Custom;

internal static class ViewportSelectionChecks
{
    public static void Run()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        using var box = new BoundingBox(-0.5, -0.5, -0.5, 0.5, 0.5, 0.5).ToBrep();
        var a = doc.Objects.AddBrep(box); var b = doc.Objects.AddBrep(box); var c = doc.Objects.AddBrep(box);
        var tree = new ModifierTreeModel();
        foreach (var id in new[] { a, b, c }) tree.RegisterSource(id);
        var root = tree.AddModifier(); var child = tree.AddModifier();
        tree.Move(child.Id, root.Id, 0, out _);
        tree.Move(tree.FindSource(a)!.Id, child.Id, 0, out _);
        tree.Move(tree.FindSource(b)!.Id, child.Id, 1, out _);
        tree.Move(tree.FindSource(c)!.Id, root.Id, 1, out _);
        bool Selected(params Guid[] ids) => doc.Objects.GetSelectedObjects(false, false).Select(obj => obj.Id).ToHashSet().SetEquals(ids);
        doc.Modified = false;
        Check(SubtreeSelection.Select(doc, tree, root.Id, out _) && Selected(a, b, c), "Viewport root selection owns every descendant");
        Check(new[] { a, b, c }.All(id => doc.Objects.FindId(id)!.IsSelected(false) == 2), "Whole-Modifier preselection persists through Gumball activation command completion");
        Check(SubtreeSelection.Select(doc, tree, child.Id, out _) && Selected(a, b), "Viewport nested selection excludes sibling cutters");
        Check(SubtreeSelection.Select(doc, tree, tree.FindSource(b)!.Id, out _) && Selected(b), "Viewport source selection replaces whole-Modifier selection");
        Check(doc.Objects.FindId(b)!.IsSelected(false) == 2, "Individual input is persistently selected for its native Gumball");
        Check(!doc.Modified && doc.Objects.Count == 3 && doc.Groups.Count == 0, "Viewport selection creates no geometry or groups and leaves the document unmodified");
        doc.Objects.Lock(a, true);
        Check(!SubtreeSelection.Select(doc, tree, root.Id, out _) && Selected(b), "Locked child rejects partial Modifier selection and preserves the previous selection");
        doc.Objects.Unlock(a, true); doc.Objects.Hide(a, true);
        Check(!SubtreeSelection.Select(doc, tree, root.Id, out _) && Selected(b), "Hidden child cannot cause a partial whole-Modifier move");
        doc.Objects.Show(a, true); doc.Objects.Delete(a, true);
        Check(!SubtreeSelection.Select(doc, tree, root.Id, out _) && Selected(b), "Missing child rejects whole selection before deselecting existing objects");

        using var pick = new PickContext { PickStyle = PickStyle.PointPick, PickLine = new Line(0, 0, 1, 0, 0, -1) };
        // Narrow X/Y aperture, with larger clip-space Z closer to the camera.
        pick.SetPickTransform(Transform.Scale(Plane.WorldXY, 100, 100, 1));
        Check(PickGeometry.Test(pick, box, true, out var depth, out _), "Shaded picking hits a preview face without a native result object");
        Check(!PickGeometry.Test(pick, box, false, out _, out _), "Wire picking does not select the invisible interior of a source face");
        using var near = box.DuplicateBrep(); near.Translate(0, 0, 0.25);
        Check(PickGeometry.Test(pick, near, true, out var nearDepth, out _) && nearDepth > depth, "Overlapping preview picking prefers the surface nearer the camera");
        using var edgeBox = box.DuplicateBrep(); edgeBox.Translate(0.5, 0, 0);
        Check(PickGeometry.Test(pick, edgeBox, false, out _, out _), "Source edges remain pickable in internal editing");
        using var away = box.DuplicateBrep(); away.Translate(2, 0, 0);
        Check(!PickGeometry.Test(pick, away, true, out _, out _), "Geometry outside the pick aperture is ignored");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
