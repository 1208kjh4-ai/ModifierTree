using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

internal static class DifferenceEvaluator
{
    // The caller owns the duplicate. Never orient or otherwise modify document-owned geometry.
    public static Brep? DuplicateInput(GeometryBase? geometry) => geometry switch
    {
        Brep brep => brep.DuplicateBrep(),
        Extrusion extrusion => extrusion.ToBrep(),
        _ => null
    };

    public static string? Validate(Brep? brep, string role)
    {
        if (brep is null) return $"{role}: source missing or not a Brep/Extrusion.";
        if (!brep.IsValid || !brep.IsSolid) return $"{role}: a valid closed solid is required.";
        return null;
    }

    public static Brep[] Evaluate(Brep first, Brep cutter, double tolerance)
        => Evaluate([first], [cutter], tolerance);

    public static Brep[] Evaluate(IEnumerable<Brep> first, IEnumerable<Brep> cutters, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0)
            throw new InvalidOperationException("Document tolerance must be positive and finite.");
        Brep[] a = [], b = [];
        try
        {
            a = DuplicateBooleanInputs(first, tolerance);
            b = DuplicateBooleanInputs(cutters, tolerance);
            return EvaluateSolidSets(a, b, tolerance);
        }
        finally
        {
            foreach (var brep in a.Concat(b)) brep.Dispose();
        }
    }

    // Inputs have already been copied, decomposed and (if needed) normalized by the caller.
    // Ownership of these input copies stays with the caller, including empty-set cases.
    internal static Brep[] EvaluateSolidSets(Brep[] first, Brep[] cutters, double tolerance)
    {
        // Empty child results are valid inputs to a parent, not Boolean failures.
        var results = first.Length == 0 ? [] : cutters.Length == 0 ? first.Select(brep => brep.DuplicateBrep()).ToArray()
            : Brep.CreateBooleanDifferenceWithIndexMap(first, cutters, tolerance, true, out _);
        if (results is null) throw new InvalidOperationException("Rhino Boolean Difference failed.");
        if (results.Any(result => !result.IsValid || !result.IsSolid))
        {
            foreach (var result in results) result.Dispose();
            throw new InvalidOperationException("Boolean returned an invalid or open result.");
        }
        return results;
    }

    internal static Brep[] DuplicateBooleanInputs(IEnumerable<Brep> inputs, double tolerance)
    {
        var solids = new List<Brep>();
        try
        {
            foreach (var input in inputs) solids.AddRange(DuplicateSolidComponents(input, tolerance));
            return solids.ToArray();
        }
        catch
        {
            foreach (var solid in solids) solid.Dispose();
            throw;
        }
    }

    // A native Boolean can silently omit untouched parts of one disconnected Brep.
    // Give it one solid per input, retaining inward cavity shells on their enclosing solid.
    // SplitDisjointPieces cannot be used here: it turns inward void shells outward.
    private static Brep[] DuplicateSolidComponents(Brep input, double tolerance)
    {
        var parts = input.GetConnectedComponents();
        if (parts.Length == 0) parts = [input.DuplicateBrep()];
        try
        {
            if (input.SolidOrientation == BrepSolidOrientation.Inward)
                foreach (var part in parts) part.Flip();
            if (parts.Length == 1) return parts;

            var outer = parts.Where(part => part.SolidOrientation == BrepSolidOrientation.Outward).ToArray();
            var inner = parts.Where(part => part.SolidOrientation == BrepSolidOrientation.Inward).ToArray();
            if (outer.Length == 0 || outer.Length + inner.Length != parts.Length)
                throw new InvalidOperationException("Cannot determine the solid boundaries of a disconnected input.");
            var ownersByCavity = new List<Brep>();
            foreach (var cavity in inner)
            {
                if (cavity.Vertices.Count == 0)
                    throw new InvalidOperationException("Cannot locate an internal cavity in the input.");
                var sample = cavity.Vertices[0].Location;
                // Classify before appending any cavities, so containment uses outer shells.
                var owners = outer.Where(solid => solid.IsPointInside(sample, tolerance, true)).ToArray();
                if (owners.Length != 1)
                    throw new InvalidOperationException("Cannot unambiguously associate an internal cavity with its solid.");
                ownersByCavity.Add(owners[0]);
            }
            for (var index = 0; index < inner.Length; index++) ownersByCavity[index].Append(inner[index]);
            foreach (var cavity in inner) cavity.Dispose();
            return outer;
        }
        catch
        {
            foreach (var part in parts) part.Dispose();
            throw;
        }
    }
}
