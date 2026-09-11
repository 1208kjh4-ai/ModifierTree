using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

internal static class ArrayAxisEdit
{
    public static bool Apply(TreeDocumentData data, Guid nodeId, int axisIndex, Vector3d direction, out string error)
    {
        error = "";
        if (axisIndex is < 0 or > 2 || !direction.IsValid || !direction.Unitize())
        { error = "Pick two distinct points to set the axis direction."; return false; }
        if (data.Tree.Find(nodeId) is not { Kind: TreeNodeKind.Array, Array: { } settings })
        { error = "Select an Array Modifier."; return false; }
        var axis = new ModifierVector(direction.X, direction.Y, direction.Z);
        var updated = axisIndex switch { 0 => settings with { AxisX = axis }, 1 => settings with { AxisY = axis }, _ => settings with { AxisZ = axis } };
        var valid = false;
        var validationError = "";
        var changed = data.Edit("Set Array axis", (tree, _) => valid = tree.SetArraySettings(nodeId, updated, out validationError), out error);
        if (validationError.Length > 0) error = validationError;
        return changed || valid && error.Length == 0;
    }
}
