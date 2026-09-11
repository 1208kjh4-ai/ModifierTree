using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

internal static class MirrorPlaneGeometry
{
    public static bool TryGetPlane(GeometryBase? geometry, double tolerance, out Plane plane)
    {
        plane = Plane.Unset;
        return geometry is Brep { IsValid: true, Faces.Count: 1 } brep &&
            brep.Faces[0].TryGetPlane(out plane, tolerance) && plane.IsValid;
    }
}
