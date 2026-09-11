using ModifierTree.Core;
using Rhino;

namespace ModifierTree.Rhino.Modifiers;

internal static class SubtreeSelection
{
    public static bool Select(RhinoDoc doc, ModifierTreeModel tree, Guid nodeId, out string error)
    {
        if (!SubtreeTransform.Validate(doc, tree, nodeId, out error)) return false;
        var objects = tree.SourcesInSubtree(nodeId).Select(id => doc.Objects.FindId(id)!).ToArray();
        if (objects.Any(obj => !obj.IsSelectable(true, false, false, false)))
        { error = "Make every source and its layer visible and unlocked before selecting this item."; return false; }
        var previous = doc.Objects.GetSelectedObjects(false, false).ToArray();
        doc.Objects.UnselectAll();
        foreach (var obj in objects)
        {
            // Keep preselection through the Gumball activation command's completion.
            if (doc.Objects.Select(obj.Id, true, true, true)) continue;
            doc.Objects.UnselectAll();
            foreach (var old in previous) old.Select(true);
            error = "Rhino could not select every source. The previous selection was restored.";
            return false;
        }
        return true;
    }
}
