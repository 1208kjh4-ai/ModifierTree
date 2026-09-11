using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Clones native sources and their complete ordered subtree in one Undo transaction.</summary>
internal static class SubtreeDuplicate
{
    public static bool Apply(RhinoDoc document, TreeDocumentData data, Guid nodeId,
        out Guid copiedNodeId, out string error)
    {
        copiedNodeId = Guid.Empty;
        error = "";
        if (data.ReadOnlyReason is { } reason) { error = reason; return false; }
        if (document.UndoActive || document.RedoActive)
        { error = "Wait for Undo or Redo to finish."; return false; }
        if (data.Tree.Find(nodeId) is not { IsModifier: true })
        { error = "Select a Modifier to duplicate."; return false; }

        var before = data.Capture();
        var wasModified = document.Modified;
        var sources = new List<SourceCopy>();
        var added = new List<Guid>();
        uint record = 0;
        try
        {
            var objectMap = new Dictionary<Guid, Guid>();
            foreach (var id in data.Tree.SourcesInSubtree(nodeId))
            {
                var source = document.Objects.FindId(id)
                    ?? throw new InvalidOperationException("Restore the missing inputs before duplicating this subtree.");
                if (source.IsReference || source.GripsOn)
                    throw new InvalidOperationException("Turn off control points and use local source objects before duplicating this subtree.");
                var geometry = source.Geometry.Duplicate();
                var attributes = source.Attributes.Duplicate();
                var copy = new SourceCopy(geometry, attributes);
                sources.Add(copy);
                if (!geometry.IsValid) throw new InvalidOperationException("A source geometry is invalid and cannot be duplicated.");
                attributes.ObjectId = Guid.NewGuid();
                attributes.Mode = ObjectMode.Normal;
                attributes.Visible = true;
                attributes.RemoveFromAllGroups();
                var layer = document.Layers[attributes.LayerIndex];
                if (!layer.IsVisible || layer.IsLocked) attributes.LayerIndex = document.Layers.CurrentLayerIndex;
                objectMap.Add(id, attributes.ObjectId);
            }

            // Validate size, source identities and the complete clone before adding any Rhino object.
            var draft = new ModifierTreeModel();
            var wires = new InputVisibility();
            TreeStateCodec.Apply(before, draft, wires);
            var preparedNodeId = draft.CloneSubtree(nodeId, objectMap).Id;
            foreach (var (original, copied) in objectMap)
                if (data.InputVisibility.IsVisible(original)) wires.Set(copied, true);
            var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
            _ = TreeStateCodec.Encode(after);

            if (!document.UndoRecordingIsActive && document.UndoRecordingEnabled)
            {
                record = document.BeginUndoRecord("Duplicate Modifier subtree");
                if (record == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
            }
            foreach (var source in sources)
            {
                var id = document.Objects.Add(source.Geometry, source.Attributes);
                if (id == Guid.Empty) throw new InvalidOperationException("Rhino could not duplicate a source object.");
                added.Add(id);
                if (id != source.Attributes.ObjectId)
                    throw new InvalidOperationException("Rhino assigned an unexpected copied source identity.");
            }
            if (!data.Edit("Duplicate Modifier subtree", (tree, visibility) =>
                { TreeStateCodec.Apply(after, tree, visibility); return true; }, out var editError))
                throw new InvalidOperationException(editError.Length == 0 ? "The copied subtree could not be registered." : editError);
            copiedNodeId = preparedNodeId;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            try
            {
                if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before);
            }
            catch { restored = false; }
            foreach (var id in added.AsEnumerable().Reverse())
            {
                try { restored &= document.Objects.Delete(id, true); }
                catch { restored = false; }
            }
            if (restored) document.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Some changes could not be rolled back; use Undo to restore this action.");
            return false;
        }
        finally
        {
            if (record != 0) document.EndUndoRecord(record);
            foreach (var source in sources) source.Dispose();
        }
    }

    private sealed record SourceCopy(GeometryBase Geometry, ObjectAttributes Attributes) : IDisposable
    {
        public void Dispose() { Geometry.Dispose(); Attributes.Dispose(); }
    }
}
