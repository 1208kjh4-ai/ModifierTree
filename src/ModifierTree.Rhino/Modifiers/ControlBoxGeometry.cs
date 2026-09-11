using System.Globalization;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>A native box Brep whose corner identities retain its local axes through document transforms.</summary>
internal static class ControlBoxGeometry
{
    // Coordinate user strings would go stale on native transforms. Vertex identities do not:
    // Rhino's affine transforms, Brep duplication and 3dm serialization retain this topology.
    private const string CornerKey = "ModifierTree.ControlBox.Corners";

    /// <summary>The frame origin is the center of the box. All dimensions must be positive.</summary>
    public static Brep Create(Plane frame, Vector3d size)
    {
        if (!frame.IsValid || !size.IsValid || size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new ArgumentException("Control Box needs a valid frame and positive finite dimensions.");
        return Create(new Box(frame, new Interval(-size.X / 2, size.X / 2),
            new Interval(-size.Y / 2, size.Y / 2), new Interval(-size.Z / 2, size.Z / 2)));
    }

    public static Brep Create(Box box)
    {
        if (!box.IsValid || box.X.Length <= 0 || box.Y.Length <= 0 || box.Z.Length <= 0)
            throw new ArgumentException("Control Box needs a valid box with positive finite dimensions.");
        var brep = box.ToBrep() ?? throw new InvalidOperationException("Could not create Control Box geometry.");
        try
        {
            var frame = box.Plane;
            var points = new[]
            {
                frame.PointAt(box.X.Min, box.Y.Min, box.Z.Min),
                frame.PointAt(box.X.Max, box.Y.Min, box.Z.Min),
                frame.PointAt(box.X.Min, box.Y.Max, box.Z.Min),
                frame.PointAt(box.X.Min, box.Y.Min, box.Z.Max)
            };
            var indices = points.Select(point => Enumerable.Range(0, brep.Vertices.Count)
                .MinBy(index => brep.Vertices[index].Location.DistanceToSquared(point))).ToArray();
            if (indices.Distinct().Count() != 4 || !brep.SetUserString(CornerKey,
                "1;" + string.Join(";", indices.Select(index => index.ToString(CultureInfo.InvariantCulture)))))
                throw new InvalidOperationException("Could not retain Control Box axis identities.");
            return brep;
        }
        catch { brep.Dispose(); throw; }
    }

    public static bool TryGetBox(GeometryBase? geometry, double tolerance, out Box box, out string error)
    {
        box = Box.Unset;
        error = "Control Box is missing or damaged. Remove it and add a new Control Box.";
        if (!double.IsFinite(tolerance) || tolerance <= 0 ||
            geometry is not Brep { IsValid: true, IsSolid: true, Vertices.Count: 8, Edges.Count: 12, Faces.Count: 6 } brep)
            return false;
        var fields = brep.GetUserString(CornerKey)?.Split(';');
        if (fields is not { Length: 5 } || fields[0] != "1") return false;
        var indices = new int[4];
        for (var i = 0; i < indices.Length; i++)
            if (!int.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out indices[i]) ||
                indices[i] is < 0 or >= 8) return false;
        if (indices.Distinct().Count() != 4) return false;

        var origin = brep.Vertices[indices[0]].Location;
        var x = brep.Vertices[indices[1]].Location - origin;
        var y = brep.Vertices[indices[2]].Location - origin;
        var z = brep.Vertices[indices[3]].Location - origin;
        var lengths = new[] { x.Length, y.Length, z.Length };
        if (lengths.Any(length => !double.IsFinite(length) || length <= tolerance))
        { error = "Control Box dimensions must be larger than the document tolerance."; return false; }
        x.Unitize(); y.Unitize(); z.Unitize();
        // Never orthogonalize a sheared parallelepiped into a different, plausible-looking box.
        const double angleTolerance = 1e-8;
        if (Math.Abs(x * y) > angleTolerance || Math.Abs(x * z) > angleTolerance || Math.Abs(y * z) > angleTolerance)
        { error = "Control Box axes must stay perpendicular. Undo the shear or add a new Control Box."; return false; }
        if (brep.Edges.Any(edge => !edge.IsLinear(tolerance)) ||
            brep.Faces.Any(face => !face.TryGetPlane(out _, tolerance))) return false;

        var plane = new Plane(origin, x, y);
        // Reversing Z is harmless to the bend definition. Keep signed X/Y identities so a
        // negative scale does not silently reverse the spine or the bend direction.
        var signedZ = plane.ZAxis * z > 0 ? lengths[2] : -lengths[2];
        var candidate = new Box(plane, new Interval(0, lengths[0]), new Interval(0, lengths[1]),
            new Interval(Math.Min(0, signedZ), Math.Max(0, signedZ)));
        if (!candidate.IsValid) return false;
        var remaining = brep.Vertices.Select(vertex => vertex.Location).ToList();
        foreach (var corner in candidate.GetCorners())
        {
            var match = remaining.FindIndex(vertex => corner.DistanceTo(vertex) <= tolerance);
            if (match < 0) return false;
            remaining.RemoveAt(match);
        }
        box = candidate;
        error = "";
        return true;
    }

    public static Plane BendFrame(Box box) => new(box.Plane.PointAt(box.X.Mid, box.Y.Min, box.Z.Mid),
        box.Plane.XAxis, box.Plane.YAxis);
}
