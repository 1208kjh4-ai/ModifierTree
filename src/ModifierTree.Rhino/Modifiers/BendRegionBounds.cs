using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Measures only the part of a solid that can undergo non-rigid Limited bending.</summary>
internal static class BendRegionBounds
{
    public static BoundingBox[] WithinInterval(Brep input, Plane frame, double height, double tolerance)
    {
        try { return MeasureInterval(input, frame, height, tolerance); }
        catch (Exception exception) { throw CannotMeasure(exception); }
    }

    private static BoundingBox[] MeasureInterval(Brep input, Plane frame, double height, double tolerance)
    {
        var toLocal = Transform.PlaneToPlane(frame, Plane.WorldXY);
        // Trim retains the half-space opposite its plane normal. These are infinite
        // Y boundaries, not the Control Box's X/Z faces: Limited is a length interval.
        // Cut just outside the active interval so intersection tolerance cannot remove
        // a thin active sliver, and roundoff at a rotated end face does not force a trim.
        var lowerBoundary = -2 * tolerance;
        var upperBoundary = height + 2 * tolerance;
        var lowerPlane = new Plane(frame.Origin + lowerBoundary * frame.YAxis, -frame.YAxis);
        var upperPlane = new Plane(frame.Origin + upperBoundary * frame.YAxis, frame.YAxis);
        var lower = TrimHalfSpace([input], lowerPlane, toLocal, lowerBoundary, keepAbove: true, tolerance);
        try
        {
            var inside = TrimHalfSpace(lower, upperPlane, toLocal, upperBoundary, keepAbove: false, tolerance);
            try { return inside.Select(piece => Bounds(piece, toLocal)).ToArray(); }
            finally { foreach (var piece in inside) piece.Dispose(); }
        }
        finally { foreach (var piece in lower) piece.Dispose(); }
    }

    // Borrow inputs; own every returned Brep. Trimming is solely for validation:
    // the caller still morphs the complete original copy so its exterior stays connected.
    private static Brep[] TrimHalfSpace(IEnumerable<Brep> inputs, Plane plane, Transform toLocal,
        double boundary, bool keepAbove, double tolerance)
    {
        var result = new List<Brep>();
        try
        {
            foreach (var input in inputs)
            {
                var bounds = Bounds(input, toLocal);
                if (keepAbove ? bounds.Max.Y <= boundary : bounds.Min.Y >= boundary) continue;
                if (keepAbove ? bounds.Min.Y >= boundary : bounds.Max.Y <= boundary)
                {
                    result.Add(input.DuplicateBrep());
                    continue;
                }

                var trimmed = input.Trim(plane, tolerance);
                // An intersecting input must yield retained geometry. An empty/failed
                // native trim is not proof that the bending interval is safe.
                if (trimmed is null || trimmed.Length == 0) throw CannotMeasure();
                result.AddRange(trimmed);
                foreach (var piece in trimmed)
                {
                    if (piece is null || !piece.IsValid || piece.Faces.Count == 0) throw CannotMeasure();
                    // Remove unused surface domains before measuring the clipped piece;
                    // otherwise a remote branch can still enlarge an underlying surface.
                    if (!piece.Faces.ShrinkFaces()) throw CannotMeasure();
                    piece.Compact();
                    _ = Bounds(piece, toLocal);
                }
            }
            return result.ToArray();
        }
        catch
        {
            foreach (var piece in result) piece?.Dispose();
            throw;
        }
    }

    private static BoundingBox Bounds(Brep piece, Transform toLocal)
    {
        if (!piece.IsValid || piece.Faces.Count == 0) throw CannotMeasure();
        var bounds = piece.GetBoundingBox(toLocal);
        if (!bounds.IsValid) throw CannotMeasure();
        return bounds;
    }

    private static InvalidOperationException CannotMeasure(Exception? innerException = null) => new(
        "Cannot verify the solid inside the Limited Bend interval. Adjust the Control Box or check the input solid.",
        innerException);
}
