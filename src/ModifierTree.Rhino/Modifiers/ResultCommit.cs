using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;

namespace ModifierTree.Rhino.Modifiers;

internal enum ResultCommitMode { Bake, Merge }

/// <summary>One native Undo record owns the fixed result, source visibility and tree snapshot.</summary>
internal static class ResultCommit
{
    public static bool Apply(RhinoDoc document, TreeDocumentData data, Guid nodeId, ResultCommitMode mode,
        out Guid resultNodeId, out string error)
    {
        resultNodeId = Guid.Empty;
        error = "";
        if (data.ReadOnlyReason is { } reason) { error = reason; return false; }
        if (document.UndoActive || document.RedoActive)
        { error = "Wait for Undo or Redo to finish."; return false; }
        if (data.Tree.Find(nodeId) is not { IsModifier: true } node)
        { error = "Select a Modifier to Bake or Merge."; return false; }

        var before = data.Capture();
        var wasModified = document.Modified;
        var sourceIds = data.Tree.SourcesInSubtree(nodeId);
        var geometryIds = data.Tree.GeometrySourcesInSubtree(nodeId);
        var hidden = new List<Guid>();
        var added = Guid.Empty;
        uint record = 0;
        try
        {
            if (geometryIds.Count == 0) throw new InvalidOperationException("This Modifier has no result to create.");
            foreach (var id in sourceIds)
            {
                var source = document.Objects.FindId(id);
                if (source is null) throw new InvalidOperationException("Restore the missing inputs before creating this result.");
                if (mode == ResultCommitMode.Merge && (source.IsLocked || source.IsReference || source.GripsOn))
                    throw new InvalidOperationException("Merge requires editable inputs. Unlock them and turn off their control points first.");
            }

            // Never bake a stale viewport cache or a transient Gumball position.
            using var evaluation = new TreeEvaluator();
            evaluation.Rebuild(data.Tree, id => document.Objects.FindId(id)?.Geometry, document.ModelAbsoluteTolerance);
            var current = evaluation.Find(nodeId) ?? throw new InvalidOperationException("This Modifier has no current result.");
            using var geometry = ResultGeometry.Create(current);
            using var attributes = document.Objects.FindId(geometryIds[0])!.Attributes.Duplicate();
            var resultObjectId = Guid.NewGuid();
            attributes.ObjectId = resultObjectId;
            attributes.Name = ModifierNames.ResultName(node);
            attributes.Mode = ObjectMode.Normal;
            attributes.Visible = true;
            attributes.RemoveFromAllGroups();
            var layer = document.Layers[attributes.LayerIndex];
            if (!layer.IsVisible || layer.IsLocked) attributes.LayerIndex = document.Layers.CurrentLayerIndex;

            // Prepare every tree change before writing any native object.
            var draft = new ModifierTreeModel();
            var visibility = new InputVisibility();
            TreeStateCodec.Apply(before, draft, visibility);
            Guid preparedNodeId;
            if (mode == ResultCommitMode.Merge)
                preparedNodeId = draft.ReplaceSubtreeWithSource(nodeId, resultObjectId).Id;
            else
            {
                draft.RegisterSource(resultObjectId);
                preparedNodeId = draft.FindSource(resultObjectId)!.Id;
                if (!draft.Move(preparedNodeId, null, 0, out var moveError)) throw new InvalidOperationException(moveError);
            }
            var after = TreeStateCodec.Capture(draft, visibility, before.PreviewEnabled);
            _ = TreeStateCodec.Encode(after);
            if (!document.UndoRecordingIsActive && document.UndoRecordingEnabled)
            {
                record = document.BeginUndoRecord(mode + " Modifier result");
                if (record == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
            }

            added = document.Objects.AddBrep(geometry, attributes);
            if (added == Guid.Empty) throw new InvalidOperationException("Rhino could not create the result object.");
            if (added != resultObjectId) throw new InvalidOperationException("Rhino assigned an unexpected result object ID.");
            if (mode == ResultCommitMode.Merge)
            {
                // Retain the originals as ordinary hidden Rhino objects; Undo restores visibility.
                foreach (var id in sourceIds)
                {
                    if (document.Objects.FindId(id)!.IsHidden) continue;
                    if (!document.Objects.Hide(id, true)) throw new InvalidOperationException("Rhino could not hide a merged input.");
                    hidden.Add(id);
                }
            }
            if (!data.Edit(mode + " Modifier result", (tree, wires) =>
                { TreeStateCodec.Apply(after, tree, wires); return true; }, out var editError))
                throw new InvalidOperationException(editError.Length == 0 ? "The result could not be registered in the tree." : editError);

            resultNodeId = preparedNodeId;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            // Also cover a notification failure after the private state was applied.
            try
            {
                if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before);
            }
            catch { restored = false; }
            foreach (var id in hidden.AsEnumerable().Reverse())
            {
                try { restored &= document.Objects.Show(id, true); }
                catch { restored = false; }
            }
            if (added != Guid.Empty)
            {
                try { restored &= document.Objects.Delete(added, true); }
                catch { restored = false; }
            }
            if (restored) document.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Some changes could not be rolled back; use Undo to restore this action.");
            return false;
        }
        finally { if (record != 0) document.EndUndoRecord(record); }
    }
}
