using Rhino.Geometry;

// Experimental only: no production Modifier or document mutation.
// Frame origin is the bottom of the box; local +Y is the spine and +X the bend direction.
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
        // 2*sin(a/2)^2 avoids cancellation of 1-cos(a) for tiny strengths.
        var halfSin = Math.Sin(angle / 2);
        var cx = 2 * halfSin * halfSin / _curvature;
        var cy = sin / _curvature;
        return _frame.PointAt(cx + x * cos + remainder * sin,
            cy - x * sin + remainder * cos, z);
    }
}
