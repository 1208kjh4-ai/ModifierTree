using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Produces independent copies, optionally consolidating Mirror's output as one solid set.</summary>
internal static class PatternEvaluator
{
    // A per-Array instance limit cannot constrain exponential growth in nested Arrays.
    internal const int MaxResultPieces = 4096;

    public static Brep[] Evaluate(ModifierTreeNode node, IReadOnlyList<IReadOnlyList<Brep>> children,
        double tolerance = 0.001, Plane? mirrorPlane = null, ArraySettings? arraySettings = null)
    {
        if (children.Count == 0) throw new InvalidOperationException("Drag at least one input into this Modifier.");
        if (children.SelectMany(child => child).Any(input => DifferenceEvaluator.Validate(input, "Input") is not null))
            throw new InvalidOperationException("A valid closed solid is required for every input.");

        var transforms = Transforms(node, mirrorPlane, arraySettings).ToArray();
        var inputCount = children.Sum(child => (long)child.Count);
        if (inputCount * transforms.Length > MaxResultPieces)
            throw new InvalidOperationException($"This pattern would exceed {MaxResultPieces:N0} result pieces. Reduce its counts or input pieces.");

        var results = new List<Brep>((int)(inputCount * transforms.Length));
        try
        {
            foreach (var transform in transforms)
            foreach (var input in children.SelectMany(child => child))
            {
                var copy = input.DuplicateBrep();
                results.Add(copy);
                // Normalize the complete solid, retaining relative orientation of its cavity shells.
                if (copy.SolidOrientation == BrepSolidOrientation.Inward) copy.Flip();
                if (!copy.Transform(transform)) throw new InvalidOperationException("Could not transform a pattern result.");
                if (transform.SimilarityType == TransformSimilarityType.OrientationReversing) copy.Flip();
                if (!copy.IsValid || !copy.IsSolid)
                    throw new InvalidOperationException("The pattern produced an invalid or open solid.");
            }
            if (node.Kind == TreeNodeKind.Mirror && node.Mirror!.Union && results.Count > 1)
            {
                // The Boolean evaluator owns its output and reports native Union failures.
                // It also retains disconnected regions and cavity orientation correctly.
                var united = BooleanEvaluator.Evaluate(TreeNodeKind.BooleanUnion,
                    results.Select(result => (IReadOnlyList<Brep>)new[] { result }).ToArray(), tolerance);
                foreach (var result in results) result.Dispose();
                results.Clear();
                return united;
            }
            return results.ToArray();
        }
        catch
        {
            foreach (var result in results) result.Dispose();
            throw;
        }
    }

    private static IEnumerable<Transform> Transforms(ModifierTreeNode node, Plane? mirrorPlane, ArraySettings? arraySettings)
    {
        if (node.Kind == TreeNodeKind.Mirror && node.Mirror is { } mirror)
        {
            if (!ModifierSettings.TryValidate(mirror, out var error)) throw new InvalidOperationException(error);
            var normal = mirrorPlane?.Normal ?? Vector(mirror.Normal);
            var origin = mirrorPlane?.Origin ?? new Point3d(mirror.Origin.X, mirror.Origin.Y, mirror.Origin.Z);
            if (!origin.IsValid || !normal.IsValid || !normal.Unitize())
                throw new InvalidOperationException("Mirror needs a finite plane origin and a nonzero normal.");
            if (mirror.KeepOriginal) yield return Transform.Identity;
            yield return Transform.Mirror(origin, normal);
        }
        else if (node.Kind == TreeNodeKind.Array && (arraySettings ?? node.Array) is { } array)
        {
            if (!ModifierSettings.TryValidate(array, out var error)) throw new InvalidOperationException(error);
            // X varies first. Index zero is the original location and is part of each count.
            for (var z = 0; z < array.CountZ; z++)
            for (var y = 0; y < array.CountY; y++)
            for (var x = 0; x < array.CountX; x++)
                yield return Transform.Translation(
                    x * array.Spacing.X * Unit(array.AxisX) +
                    y * array.Spacing.Y * Unit(array.AxisY) +
                    z * array.Spacing.Z * Unit(array.AxisZ));
        }
        else throw new InvalidOperationException("Unsupported pattern Modifier or missing settings.");
    }

    private static Vector3d Vector(ModifierVector value) => new(value.X, value.Y, value.Z);
    private static Vector3d Unit(ModifierVector value)
    {
        var vector = Vector(value);
        if (!vector.Unitize()) throw new InvalidOperationException("Array axes need nonzero directions.");
        return vector;
    }
}
