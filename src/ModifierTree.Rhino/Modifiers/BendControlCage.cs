using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>
/// Disposable viewport/pick wires for one Control Box's own bend. The native box remains
/// the authoritative frame and dimensions; these curves never participate in tree evaluation.
/// </summary>
internal sealed class BendControlCage : IDisposable
{
    private bool _disposed;

    private BendControlCage(List<Curve> wires, BoundingBox bounds)
    {
        Wires = wires.AsReadOnly();
        Bounds = bounds;
    }

    public IReadOnlyList<Curve> Wires { get; }
    public BoundingBox Bounds { get; }

    /// <summary>
    /// A dynamic transform acts on a duplicate of the raw box before deriving the bend.
    /// In particular, nonuniform scaling must recalculate curvature rather than scale a bent cage.
    /// Returns null for an invalid box, settings or transform, without modifying the source.
    /// </summary>
    public static BendControlCage? Create(GeometryBase rawBox, ControlBoxSettings settings,
        double tolerance, Transform? dynamicTransform = null)
    {
        if (!ModifierSettings.TryValidate(settings, out _) || !double.IsFinite(tolerance) || tolerance <= 0)
            return null;
        GeometryBase? transformed = null;
        try
        {
            var geometry = rawBox;
            if (dynamicTransform is { } transform)
            {
                if (!transform.IsValid || !transform.IsAffine || !transform.TryGetInverse(out _)) return null;
                transformed = rawBox.Duplicate();
                if (!transformed.Transform(transform)) return null;
                geometry = transformed;
            }
            if (!ControlBoxGeometry.TryGetBox(geometry, tolerance, out var box, out _)) return null;
            return CreateWires(box, settings, tolerance);
        }
        finally { transformed?.Dispose(); }
    }

    private static BendControlCage? CreateWires(Box box, ControlBoxSettings settings, double tolerance)
    {
        var frame = ControlBoxGeometry.BendFrame(box);
        var height = box.Y.Length;
        var width = box.X.Length / 2;
        var depth = box.Z.Length / 2;
        var morph = new LengthPreservingBend(frame, height, settings.Strength * Math.PI / 180,
            settings.Limited, tolerance);
        var wires = new List<Curve>(14);
        var retained = false;
        try
        {
            // A rail is a circular arc at fixed local X/Z. Unlike fitting a morphed Brep,
            // this gives smooth, inexpensive display curves even at 180 degrees.
            foreach (var x in new[] { -width, width })
            foreach (var z in new[] { -depth, depth }) AddRail(x, z);
            AddRail(0, 0);

            var intervals = Math.Max(4, (int)Math.Ceiling(Math.Abs(settings.Strength) / 22.5));
            for (var i = 0; i <= intervals; i++)
            {
                var y = height * i / intervals;
                wires.Add(new PolylineCurve(new[]
                {
                    Point(-width, y, -depth), Point(width, y, -depth),
                    Point(width, y, depth), Point(-width, y, depth), Point(-width, y, -depth)
                }));
            }

            var bounds = BoundingBox.Empty;
            foreach (var wire in wires)
            {
                if (!wire.IsValid) return null;
                var curveBounds = wire.GetBoundingBox(true);
                if (!curveBounds.IsValid) return null;
                bounds.Union(curveBounds);
            }
            if (!bounds.IsValid) return null;
            retained = true;
            return new BendControlCage(wires, bounds);
        }
        finally { if (!retained) foreach (var wire in wires) wire.Dispose(); }

        Point3d Point(double x, double y, double z) => morph.MorphPoint(frame.PointAt(x, y, z));

        void AddRail(double x, double z)
        {
            var start = Point(x, 0, z);
            var middle = Point(x, height / 2, z);
            var end = Point(x, height, z);
            var epsilon = tolerance / 100;
            // A decorative rail can lie exactly on the curvature center. Omit its zero-length
            // curve; the cross sections still show the folded cage. Input safety is evaluated separately.
            if (start.DistanceTo(end) <= epsilon && start.DistanceTo(middle) <= epsilon) return;
            var chordMiddle = start + (end - start) / 2;
            if (middle.DistanceTo(chordMiddle) <= epsilon)
            {
                wires.Add(new LineCurve(start, end));
                return;
            }
            var arc = new Arc(start, middle, end);
            if (arc.IsValid) wires.Add(new ArcCurve(arc));
            else
            {
                // Extreme coordinates can make the three-point circle ill-conditioned.
                // A bounded fallback keeps the control usable without asking Rhino to fit a surface.
                var points = new Point3d[65];
                for (var i = 0; i < points.Length; i++) points[i] = Point(x, height * i / (points.Length - 1), z);
                wires.Add(new PolylineCurve(points));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var wire in Wires) wire.Dispose();
    }
}
