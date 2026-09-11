using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Transforms pattern frames without letting object translation affect their directions.</summary>
internal static class PatternFrameTransform
{
    public static IEnumerable<ModifierTreeNode> ArraysInSubtree(ModifierTreeModel tree, Guid nodeId)
    {
        if (tree.Find(nodeId) is not { IsModifier: true } node) yield break;
        if (node.Kind == TreeNodeKind.Array) yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in ArraysInSubtree(tree, child)) yield return descendant;
    }

    public static bool TryTransform(ArraySettings settings, Transform transform, out ArraySettings transformed, out string error)
    {
        transformed = settings;
        error = "Array axes require a finite, nonsingular affine transform.";
        if (!transform.IsValid || !transform.IsAffine) return false;
        if (!ModifierSettings.TryValidate(settings, out error)) return false;
        if (transform == Transform.Translation(transform.M03, transform.M13, transform.M23)) return true;
        if (!TryAxis(settings.AxisX, settings.Spacing.X, transform, out var x, out var sx) ||
            !TryAxis(settings.AxisY, settings.Spacing.Y, transform, out var y, out var sy) ||
            !TryAxis(settings.AxisZ, settings.Spacing.Z, transform, out var z, out var sz)) return false;
        var candidate = settings with { AxisX = x, AxisY = y, AxisZ = z, Spacing = new ModifierVector(sx, sy, sz) };
        if (!ModifierSettings.TryValidate(candidate, out error)) return false;
        transformed = candidate;
        return true;
    }

    private static bool TryAxis(ModifierVector axis, double spacing, Transform transform, out ModifierVector direction, out double distance)
    {
        direction = axis;
        distance = spacing;
        var vector = new Vector3d(axis.X, axis.Y, axis.Z);
        if (!vector.Unitize()) return false;
        vector.Transform(transform);
        var length = vector.Length;
        if (!vector.IsValid || !double.IsFinite(length) || length < 1e-12 || !vector.Unitize()) return false;
        direction = new ModifierVector(vector.X, vector.Y, vector.Z);
        distance = spacing * length;
        return double.IsFinite(distance);
    }
}
