using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;

namespace ModifierTree.Rhino.Modifiers;

internal static class TreeItemRemoval
{
    public static bool Apply(RhinoDoc doc, TreeDocumentData data, Guid nodeId, out string error)
    {
        error = "";
        if (data.ReadOnlyReason is { } reason) { error = reason; return false; }
        if (doc.UndoActive || doc.RedoActive) { error = "Wait for Undo or Redo to finish."; return false; }
        var before = data.Capture();
        var draft = new ModifierTreeModel();
        var wires = new InputVisibility();
        TreeStateCodec.Apply(before, draft, wires);
        if (!draft.Remove(nodeId)) { error = "This tree item cannot be removed directly."; return false; }
        var controls = data.Tree.Nodes.Where(node => node.IsControl && draft.Find(node.Id) is null)
            .Select(node => node.ObjectId!.Value).ToArray();
        if (controls.Any(id => doc.Objects.FindId(id) is { } obj && (obj.IsLocked || obj.IsReference)))
        { error = "Unlock the control objects before removing this tree item."; return false; }
        var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
        _ = TreeStateCodec.Encode(after);
        var hidden = new List<Guid>();
        var wasModified = doc.Modified;
        uint undo = 0;
        try
        {
            if (doc.UndoRecordingEnabled && !doc.UndoRecordingIsActive)
            {
                undo = doc.BeginUndoRecord("Remove Modifier Tree item");
                if (undo == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
            }
            foreach (var id in controls)
            {
                if (doc.Objects.FindId(id) is not { IsHidden: false }) continue;
                if (!doc.Objects.Hide(id, true)) throw new InvalidOperationException("Rhino could not hide the removed control object.");
                hidden.Add(id);
            }
            if (!data.Edit("Remove Modifier Tree item", (tree, visibility) =>
                { TreeStateCodec.Apply(after, tree, visibility); return true; }, out error))
                throw new InvalidOperationException(error.Length > 0 ? error : "Could not remove the tree item.");
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            try { if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before); }
            catch { restored = false; }
            foreach (var id in hidden)
                try { restored &= doc.Objects.Show(id, true); } catch { restored = false; }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Use Undo to restore this action.");
            return false;
        }
        finally { if (undo != 0) doc.EndUndoRecord(undo); }
    }
}
