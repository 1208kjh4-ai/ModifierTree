using System.Drawing;
using Rhino.Display;
using Rhino.DocObjects;

namespace ModifierTree.Rhino.Display;

internal static class PreviewAppearance
{
    // Conduit display follows basic viewport settings. This is not a render-engine object:
    // ray tracing, texture mapping and advanced technical display effects require a later result-object layer.
    public static DisplayMaterial Material(DisplayPipelineAttributes attributes, RhinoObject? primary, Color color, bool current)
    {
        if (!current) return new DisplayMaterial(Color.FromArgb(235, 165, 70), 0.4);
        var material = attributes.UseAssignedObjectMaterial && primary is not null
            ? new DisplayMaterial(primary.GetMaterial(true)) : new DisplayMaterial(color);
        if (attributes.UseCustomObjectColor) material.Diffuse = attributes.ObjectColor;
        else if (attributes.UseCustomObjectMaterial)
            material.Diffuse = attributes.FrontDiffuse;
        if (attributes.UseCustomObjectMaterial || attributes.FrontOverrideObjectTransparency)
            // Rhino 8.34 returns a 0..1 value (verified against the native API),
            // despite the installed XML documentation describing a percentage.
            material.Transparency = Math.Clamp(attributes.FrontMaterialTransparency, 0, 1);
        if (attributes.DisableTransparency) material.Transparency = 0;
        return material;
    }
}
