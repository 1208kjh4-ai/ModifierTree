using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Creates one independent solid Brep object from a current Modifier result.</summary>
internal static class ResultGeometry
{
    // The caller owns the returned Brep. Cached preview and document geometry stay untouched.
    public static Brep Create(NodeEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (!evaluation.IsCurrent || !evaluation.HasResult)
            throw new InvalidOperationException("The Modifier needs a current valid result before Bake or Merge.");
        return Create(evaluation.Results);
    }

    public static Brep Create(IReadOnlyList<Brep> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
            throw new InvalidOperationException("The Modifier result is empty; there is no object to Bake or Merge.");
        foreach (var result in results)
            if (result is null || !result.IsValid || !result.IsSolid)
                throw new InvalidOperationException("Bake and Merge require valid closed solid results.");

        Brep? combined = null;
        try
        {
            foreach (var result in results)
            {
                var part = result.DuplicateBrep();
                if (combined is null) combined = part;
                else
                {
                    // Append copies topology without welding or dropping disconnected pieces.
                    // Rhino can store all these closed shells as one selectable Brep object.
                    // Preserve shell orientation: an inward shell can bound an internal void.
                    using (part) combined.Append(part);
                }
            }
            if (combined is null || !combined.IsValid || !combined.IsSolid)
                throw new InvalidOperationException("The Modifier result could not be made into one valid solid object.");
            return combined;
        }
        catch
        {
            combined?.Dispose();
            throw;
        }
    }
}
