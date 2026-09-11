using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Rhino.Input.Custom;

namespace ModifierTree.Rhino.Modifiers;

internal static class PickGeometry
{
    public static bool TestCurves(PickContext pick, IReadOnlyList<Curve> curves, BoundingBox bounds,
        out double depth, out double distance)
    {
        depth = double.NegativeInfinity;
        distance = double.PositiveInfinity;
        if (!bounds.IsValid || !pick.PickFrustumTest(bounds, out _)) return false;
        var hit = false;
        foreach (var wire in curves)
        {
            using var curve = wire.ToNurbsCurve();
            if (!pick.PickFrustumTest(curve, out _, out var d, out var gap)) continue;
            if (!hit || gap < distance || Math.Abs(gap - distance) < 1e-8 && d > depth)
            { depth = d; distance = gap; }
            hit = true;
        }
        return hit;
    }

    public static bool Test(PickContext pick, Brep brep, bool faces, out double depth, out double distance, double tolerance = 0.001)
    {
        depth = double.NegativeInfinity;
        distance = double.PositiveInfinity;
        if (!pick.PickFrustumTest(brep.GetBoundingBox(false), out _)) return false;
        var hit = false;
        foreach (var edge in brep.Edges)
        {
            using var curve = edge.ToNurbsCurve();
            if (!pick.PickFrustumTest(curve, out _, out var d, out var gap)) continue;
            if (!hit || (faces ? d > depth : gap < distance || Math.Abs(gap - distance) < 1e-8 && d > depth))
            { depth = d; distance = gap; }
            hit = true;
        }
        if (!faces) return hit;
        // Test the actual Brep along the view ray. Mesh picking requires a native
        // display pipeline and also approximates curved preview faces.
        using var ray = new LineCurve(pick.PickLine);
        Intersection.CurveBrep(ray, brep, tolerance, out var overlaps, out var points);
        try
        {
            if (points is not null)
                foreach (var point in points)
                    if (pick.PickFrustumTest(point, out var d, out var gap) && (!hit || d > depth))
                    { depth = d; distance = gap; hit = true; }
        }
        finally { if (overlaps is not null) foreach (var curve in overlaps) curve.Dispose(); }
        return hit;
    }
}
