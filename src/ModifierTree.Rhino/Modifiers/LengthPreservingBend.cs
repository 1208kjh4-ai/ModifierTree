using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Preserves arc length on local X=0, bending local +Y toward +X. Frame origin is the spine bottom.</summary>
internal sealed class LengthPreservingBend : SpaceMorph
{
    private readonly Plane _frame;
    private readonly double _height;
    private readonly double _curvature;
    private readonly bool _limited;

    public LengthPreservingBend(Plane frame, double height, double radians, bool limited, double tolerance)
    {
        if (!frame.IsValid || !double.IsFinite(height) || height <= 0 ||
            !double.IsFinite(radians) || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentException("A valid frame, positive height/tolerance and finite strength are required.");
        _frame = frame;
        _height = height;
        _curvature = radians / height;
        _limited = limited;
        Tolerance = tolerance;
        PreserveStructure = false;
        QuickPreview = false;
    }

    public override Point3d MorphPoint(Point3d point)
    {
        if (_curvature == 0) return point;
        var offset = point - _frame.Origin;
        var x = offset * _frame.XAxis;
        var y = offset * _frame.YAxis;
        var z = offset * _frame.ZAxis;
        var s = _limited ? Math.Clamp(y, 0, _height) : y;
        var remainder = y - s;
        var angle = _curvature * s;
        var sin = Math.Sin(angle);
        var cos = Math.Cos(angle);
        // Stable near zero strength: 1-cos(a) loses significant digits there.
        var halfSin = Math.Sin(angle / 2);
        var cx = 2 * halfSin * halfSin / _curvature;
        var cy = sin / _curvature;
        return _frame.PointAt(cx + x * cos + remainder * sin,
            cy - x * sin + remainder * cos, z);
    }
}
