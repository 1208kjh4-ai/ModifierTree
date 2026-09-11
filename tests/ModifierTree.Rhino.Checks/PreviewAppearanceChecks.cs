using System.Drawing;
using ModifierTree.Rhino.Display;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class PreviewAppearanceChecks
{
    public static void Run()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        using var box = new BoundingBox(0, 0, 0, 1, 1, 1).ToBrep();
        using var objectAttributes = new ObjectAttributes { ObjectColor = Color.Red, ColorSource = ObjectColorSource.ColorFromObject };
        var id = doc.Objects.AddBrep(box, objectAttributes);
        var source = doc.Objects.FindId(id)!;
        using var mode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
        var attributes = mode.DisplayAttributes;
        attributes.UseAssignedObjectMaterial = false;
        attributes.UseCustomObjectColor = false;
        attributes.UseCustomObjectMaterial = false;
        attributes.FrontOverrideObjectColor = false;
        attributes.FrontOverrideObjectTransparency = false;
        using (var material = PreviewAppearance.Material(attributes, source, source.Attributes.DrawColor(doc), true))
            Check(material.Diffuse.ToArgb() == Color.Red.ToArgb(), "Preview uses primary source object color instead of fixed cyan");
        attributes.UseCustomObjectColor = true;
        attributes.ObjectColor = Color.Blue;
        using (var material = PreviewAppearance.Material(attributes, source, Color.Red, true))
            Check(material.Diffuse.ToArgb() == Color.Blue.ToArgb(), "Viewport custom object color overrides source color");
        attributes.FrontOverrideObjectTransparency = true;
        attributes.FrontMaterialTransparency = 0.65;
        attributes.DisableTransparency = false;
        using (var material = PreviewAppearance.Material(attributes, source, Color.Red, true))
            Check(Math.Abs(material.Transparency - 0.65) < 0.001, "Viewport transparency maps to display material using native 0..1 range");
        attributes.DisableTransparency = true;
        using (var material = PreviewAppearance.Material(attributes, source, Color.Red, true))
            Check(material.Transparency == 0, "Viewport transparency disable is respected");
        using (var material = PreviewAppearance.Material(attributes, source, Color.Red, false))
            Check(material.Diffuse.R == 235 && material.Diffuse.G == 165, "Stale results retain their failure color");
        Console.WriteLine("INFO: Material rules verified; actual viewport shading, edges and render effects require UI checks.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
