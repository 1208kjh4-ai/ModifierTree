using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>One complete property edit owns one private-data Undo record.</summary>
internal static class ModifierPropertyEdit
{
    public static bool SetEnabled(TreeDocumentData data, Guid nodeId, bool enabled, out string error)
    {
        var valid = false;
        var validationError = "";
        var changed = data.Edit(enabled ? "Enable Modifier" : "Disable Modifier", (tree, _) =>
            valid = tree.SetModifierEnabled(nodeId, enabled, out validationError), out error);
        if (validationError.Length > 0) error = validationError;
        return changed || valid && error.Length == 0;
    }

    public static bool Apply(TreeDocumentData data, Guid nodeId, string name, bool enabled,
        MirrorSettings? mirror, ArraySettings? array, out string error)
    {
        var valid = false;
        var validationError = "";
        var changed = data.Edit("Edit Modifier properties", (tree, _) =>
        {
            if (tree.Find(nodeId) is not { IsModifier: true } node)
            { validationError = "Select a Modifier to edit."; return false; }
            if ((node.Kind == TreeNodeKind.Mirror) != (mirror is not null) ||
                (node.Kind == TreeNodeKind.Array) != (array is not null))
            { validationError = "The properties do not match this Modifier type."; return false; }
            if (!tree.RenameModifier(nodeId, name, out validationError) ||
                !tree.SetModifierEnabled(nodeId, enabled, out validationError)) return false;
            if (mirror is not null && !tree.SetMirrorSettings(nodeId, mirror, out validationError)) return false;
            if (array is not null && !tree.SetArraySettings(nodeId, array, out validationError)) return false;
            valid = true;
            return true;
        }, out error);
        if (validationError.Length > 0) error = validationError;
        return changed || valid && error.Length == 0;
    }
}
