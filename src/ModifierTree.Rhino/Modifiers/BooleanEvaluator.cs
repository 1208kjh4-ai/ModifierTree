using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Each child is a solid set, including disconnected or empty results.</summary>
internal static class BooleanEvaluator
{
    // The caller owns every returned Brep; cached and document-owned inputs are never changed.
    public static Brep[] Evaluate(TreeNodeKind kind, IReadOnlyList<IReadOnlyList<Brep>> children, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count < 2)
            throw new InvalidOperationException("A Boolean Modifier requires at least two inputs.");
        if (!double.IsFinite(tolerance) || tolerance <= 0)
            throw new InvalidOperationException("Document tolerance must be positive and finite.");
        if (kind == TreeNodeKind.BooleanDifference)
            return Difference(children, tolerance);
        foreach (var child in children)
        {
            ArgumentNullException.ThrowIfNull(child);
            foreach (var input in child)
                if (DifferenceEvaluator.Validate(input, "Input") is { } error)
                    throw new InvalidOperationException(error);
        }
        return kind switch
        {
            TreeNodeKind.BooleanUnion => Union(children, tolerance),
            TreeNodeKind.BooleanIntersection => Intersect(children, tolerance),
            _ => throw new InvalidOperationException("Unsupported Boolean Modifier.")
        };
    }

    private static Brep[] Difference(IReadOnlyList<IReadOnlyList<Brep>> children, double tolerance)
    {
        Brep[] first = [], cutters = [];
        try
        {
            first = NormalizeSolidSet(children[0], tolerance);
            cutters = NormalizeSolidSet(children.Skip(1).SelectMany(child => child), tolerance);
            return DifferenceEvaluator.EvaluateSolidSets(first, cutters, tolerance);
        }
        finally { Dispose(first); Dispose(cutters); }
    }

    private static Brep[] Union(IReadOnlyList<IReadOnlyList<Brep>> children, double tolerance)
    {
        var inputs = DifferenceEvaluator.DuplicateBooleanInputs(children.SelectMany(child => child), tolerance);
        try
        {
            var unique = UniqueSolids(inputs, tolerance);
            // Empty children contribute nothing. A lone solid still needs its own result copy.
            return Validated(unique.Length switch
            {
                0 => [],
                1 => [unique[0].DuplicateBrep()],
                _ => Brep.CreateBooleanUnion(unique, tolerance, true)
            }, "Union", tolerance);
        }
        finally { Dispose(inputs); }
    }

    private static Brep[] Intersect(IReadOnlyList<IReadOnlyList<Brep>> children, double tolerance)
    {
        var current = NormalizeSolidSet(children[0], tolerance);
        try
        {
            // Intersect with each whole child set, not with each disconnected piece in it.
            // Flattening children would incorrectly remove disjoint pieces of one input.
            foreach (var child in children.Skip(1))
            {
                if (current.Length == 0) break;
                var nextInput = NormalizeSolidSet(child, tolerance);
                try
                {
                    var next = IntersectSolidSets(current, nextInput, tolerance);
                    Dispose(current);
                    current = next;
                }
                finally { Dispose(nextInput); }
            }
            var result = current;
            current = [];
            return result;
        }
        finally { Dispose(current); }
    }

    private static Brep[] NormalizeSolidSet(IEnumerable<Brep> input, double tolerance)
    {
        var solids = DifferenceEvaluator.DuplicateBooleanInputs(input, tolerance);
        // Pattern results intentionally preserve individual copies. At a Boolean
        // boundary those copies represent their union, including materialized
        // compound Breps. Pairwise intersection must not count overlaps twice.
        if (solids.Length < 2) return solids;
        var bounds = solids.Select(solid => solid.GetBoundingBox(true)).ToArray();
        var mayOverlap = false;
        for (var first = 0; first < bounds.Length && !mayOverlap; first++)
        for (var second = first + 1; second < bounds.Length; second++)
        {
            var a = bounds[first];
            var b = bounds[second];
            if (Math.Min(a.Max.X, b.Max.X) + tolerance < Math.Max(a.Min.X, b.Min.X) ||
                Math.Min(a.Max.Y, b.Max.Y) + tolerance < Math.Max(a.Min.Y, b.Min.Y) ||
                Math.Min(a.Max.Z, b.Max.Z) + tolerance < Math.Max(a.Min.Z, b.Min.Z)) continue;
            mayOverlap = true;
            break;
        }
        if (!mayOverlap) return solids;
        try
        {
            var unique = UniqueSolids(solids, tolerance);
            return unique.Length == 1 ? [unique[0].DuplicateBrep()]
                : Validated(Brep.CreateBooleanUnion(unique, tolerance, true), "Union", tolerance);
        }
        finally { Dispose(solids); }
    }

    private static Brep[] IntersectSolidSets(Brep[] first, Brep[] second, double tolerance)
    {
        var results = new List<Brep>();
        try
        {
            foreach (var a in first)
                foreach (var b in second)
                    results.AddRange(IntersectSolids(a, b, tolerance));
            return results.ToArray();
        }
        catch { Dispose(results); throw; }
    }

    private static Brep[] IntersectSolids(Brep first, Brep second, double tolerance)
    {
        if (SameSolid(first, second, tolerance)) return [first.DuplicateBrep()];
        var a = first.GetConnectedComponents();
        var b = second.GetConnectedComponents();
        if (a.Length == 0) a = [first.DuplicateBrep()];
        if (b.Length == 0) b = [second.DuplicateBrep()];
        try
        {
            if (a.Length <= 1 && b.Length <= 1)
                return IntersectBoundaries(first, second, tolerance);

            // Rhino intersects each disconnected boundary of an appended hollow Brep
            // independently, which can fill its cavity or even include outside volume.
            // Intersect the enclosing boundaries, then remove the union of their voids.
            var outerA = a.Single(shell => shell.SolidOrientation == BrepSolidOrientation.Outward);
            var outerB = b.Single(shell => shell.SolidOrientation == BrepSolidOrientation.Outward);
            var cavities = a.Concat(b).Where(shell => shell.SolidOrientation == BrepSolidOrientation.Inward).ToArray();
            foreach (var cavity in cavities) cavity.Flip();
            cavities = UniqueSolids(cavities, tolerance);
            var bodies = IntersectBoundaries(outerA, outerB, tolerance);
            Brep[] voids = [];
            try
            {
                if (bodies.Length == 0) return [];
                voids = cavities.Length == 1 ? [cavities[0].DuplicateBrep()]
                    : Validated(Brep.CreateBooleanUnion(cavities, tolerance, true), "Union", tolerance);
                var results = new List<Brep>();
                try
                {
                    foreach (var body in bodies) results.AddRange(ExcludeVoids(body, voids, tolerance));
                    return results.ToArray();
                }
                catch { Dispose(results); throw; }
            }
            finally { Dispose(bodies); Dispose(voids); }
        }
        finally { Dispose(a); Dispose(b); }
    }

    private static Brep[] IntersectBoundaries(Brep first, Brep second, double tolerance)
    {
        if (SameSolid(first, second, tolerance)) return [first.DuplicateBrep()];
        var a = first.GetBoundingBox(true);
        var b = second.GetBoundingBox(true);
        if (Math.Min(a.Max.X, b.Max.X) <= Math.Max(a.Min.X, b.Min.X) ||
            Math.Min(a.Max.Y, b.Max.Y) <= Math.Max(a.Min.Y, b.Min.Y) ||
            Math.Min(a.Max.Z, b.Max.Z) <= Math.Max(a.Min.Z, b.Min.Z)) return [];
        var native = Brep.CreateBooleanIntersection(first, second, tolerance);
        if (native is { Length: > 0 }) return Validated(native, "Intersection", tolerance);
        if (!BoundariesIntersect(first, second, tolerance))
        {
            if (second.IsPointInside(BoundaryPoint(first), tolerance, true)) return [first.DuplicateBrep()];
            if (first.IsPointInside(BoundaryPoint(second), tolerance, true)) return [second.DuplicateBrep()];
            return [];
        }
        return Validated(native, "Intersection", tolerance);
    }

    private static Brep[] ExcludeVoids(Brep body, Brep[] voids, double tolerance)
    {
        var crossing = new List<Brep>();
        var enclosed = new List<Brep>();
        foreach (var cavity in voids)
        {
            if (SameSolid(body, cavity, tolerance)) return [];
            if (BoundariesIntersect(body, cavity, tolerance)) crossing.Add(cavity);
            else if (cavity.IsPointInside(BoundaryPoint(body), tolerance, true)) return [];
            else if (body.IsPointInside(BoundaryPoint(cavity), tolerance, true)) enclosed.Add(cavity);
        }
        var results = crossing.Count == 0 ? [body.DuplicateBrep()]
            : Validated(Brep.CreateBooleanDifferenceWithIndexMap([body], crossing, tolerance, true, out _), "Difference", tolerance);
        try
        {
            // A fully enclosed cutter has no intersection curves. Rhino's native
            // Difference may ignore it, so attach its inward boundary after clipping.
            // Union above makes these cavity volumes disjoint before containment tests.
            foreach (var cavity in enclosed)
            {
                var sample = BoundaryPoint(cavity);
                var owners = results.Where(result => result.IsPointInside(sample, tolerance, true)).ToArray();
                if (owners.Length != 1)
                    throw new InvalidOperationException("Cannot associate the intersection's cavity with its solid.");
                using var inward = cavity.DuplicateBrep();
                inward.Flip();
                owners[0].Append(inward);
            }
            if (results.Any(result => !result.IsValid || !result.IsSolid))
                throw new InvalidOperationException("Intersection returned an invalid or open result.");
            return results;
        }
        catch { Dispose(results); throw; }
    }

    private static bool BoundariesIntersect(Brep first, Brep second, double tolerance)
    {
        var success = global::Rhino.Geometry.Intersect.Intersection.BrepBrep(first, second, tolerance, out var curves, out var points);
        try
        {
            if (!success) throw new InvalidOperationException("Cannot determine the relationship between solid boundaries.");
            return curves.Length > 0 || points.Length > 0;
        }
        finally { foreach (var curve in curves) curve.Dispose(); }
    }

    private static Point3d BoundaryPoint(Brep brep)
    {
        var point = brep.Vertices.Count > 0 ? brep.Vertices[0].Location : brep.ClosestPoint(brep.GetBoundingBox(true).Center);
        if (!point.IsValid) throw new InvalidOperationException("Cannot locate a point on the solid boundary.");
        return point;
    }

    private static Brep[] UniqueSolids(IEnumerable<Brep> inputs, double tolerance)
    {
        var unique = new List<Brep>();
        foreach (var input in inputs)
            if (!unique.Any(existing => SameSolid(existing, input, tolerance))) unique.Add(input);
        return unique.ToArray();
    }

    private static bool SameSolid(Brep first, Brep second, double tolerance)
    {
        if (GeometryBase.GeometryEquals(first, second) || first.IsDuplicate(second, tolerance)) return true;
        if (first.Faces.Count != second.Faces.Count) return false;
        var a = first.GetBoundingBox(true);
        var b = second.GetBoundingBox(true);
        if (a.Min.DistanceTo(b.Min) > tolerance || a.Max.DistanceTo(b.Max) > tolerance) return false;

        // Extracting a cavity can reorder Brep topology even when every trimmed face
        // is unchanged. Compare the complete face sets so an exact cavity match is
        // recognized without inferring equality from sampled points or volume.
        var remaining = second.Faces.Select(face => face.DuplicateFace(false)).ToList();
        try
        {
            foreach (var face in first.Faces)
            {
                using var candidate = face.DuplicateFace(false);
                var index = remaining.FindIndex(other => GeometryBase.GeometryEquals(candidate, other) ||
                    candidate.IsDuplicate(other, tolerance));
                if (index < 0) return false;
                remaining[index].Dispose();
                remaining.RemoveAt(index);
            }
            return true;
        }
        finally { Dispose(remaining); }
    }

    private static Brep[] Validated(Brep[]? results, string operation, double tolerance)
    {
        if (results is null) throw new InvalidOperationException($"Rhino Boolean {operation} failed.");
        if (results.Any(result => result is null || !result.IsValid || !result.IsSolid))
        {
            Dispose(results);
            throw new InvalidOperationException("Boolean returned an invalid or open result.");
        }
        if (!results.Any(result => result.SolidOrientation == BrepSolidOrientation.Inward)) return results;

        // Native Booleans can return a cavity shell as a separate inward Brep. It belongs
        // to its enclosing solid, so group the complete output before any parent can
        // normalize that shell alone and accidentally turn its void into filled volume.
        try
        {
            using var combined = ResultGeometry.Create(results);
            return DifferenceEvaluator.DuplicateBooleanInputs([combined], tolerance);
        }
        finally { Dispose(results); }
    }

    private static void Dispose(IEnumerable<Brep> values)
    {
        foreach (var value in values) value?.Dispose();
    }
}
