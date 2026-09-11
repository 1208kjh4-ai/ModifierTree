using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>The control is a native planar Brep; its geometry is the authoritative reflection plane.</summary>
internal static class MirrorPlaneEdit
{
    public static bool AddMirror(RhinoDoc doc, TreeDocumentData data, out Guid nodeId, out string error)
        => Apply(doc, data, null, new Plane(Point3d.Origin, Vector3d.XAxis), out nodeId, out error);

    public static bool SetPlane(RhinoDoc doc, TreeDocumentData data, Guid nodeId, Plane plane, out string error)
        => Apply(doc, data, nodeId, plane, out _, out error);

    private static bool Apply(RhinoDoc doc, TreeDocumentData data, Guid? nodeId, Plane plane,
        out Guid resultId, out string error)
    {
        resultId = Guid.Empty;
        error = "";
        if (data.ReadOnlyReason is { } reason) { error = reason; return false; }
        if (doc.UndoActive || doc.RedoActive) { error = "Wait for Undo or Redo to finish."; return false; }
        if (!plane.IsValid) { error = "Pick three distinct, non-collinear points for the plane."; return false; }
        if (nodeId is { } requested && data.Tree.Find(requested)?.Kind != TreeNodeKind.Mirror)
        { error = "Select a Mirror Modifier."; return false; }

        var before = data.Capture();
        var wasModified = doc.Modified;
        GeometryBase? original = null;
        Guid objectId = Guid.Empty;
        bool added = false, replaced = false;
        uint undo = 0;
        try
        {
            var draft = new ModifierTreeModel();
            var wires = new InputVisibility();
            TreeStateCodec.Apply(before, draft, wires);
            var mirror = nodeId is { } id ? draft.Find(id)! : draft.AddModifier(TreeNodeKind.Mirror);
            var control = mirror.Children.Select(draft.Find).FirstOrDefault(child => child?.IsControl == true);
            var oldObject = control?.ObjectId is { } oldId ? doc.Objects.FindId(oldId) : null;
            if (oldObject is not null && (oldObject.IsLocked || oldObject.IsReference || oldObject.GripsOn || oldObject.IsHidden))
                throw new InvalidOperationException("Make BasePlane visible and editable before setting its plane.");

            var bounds = BoundingBox.Empty;
            foreach (var source in draft.GeometrySourcesInSubtree(mirror.Id))
                if (doc.Objects.FindId(source) is { } obj) bounds.Union(obj.Geometry.GetBoundingBox(true));
            var size = bounds.IsValid ? Math.Clamp(bounds.Diagonal.Length * 0.6, 1, 1e6) : 10;
            using var surface = new PlaneSurface(plane, new Interval(-size, size), new Interval(-size, size));
            using var geometry = surface.ToBrep();
            if (!geometry.IsValid) throw new InvalidOperationException("Rhino could not construct the BasePlane.");
            objectId = oldObject?.Id ?? Guid.NewGuid();
            if (!draft.SetBasePlane(mirror.Id, objectId, out error)) return false;
            var settings = mirror.Mirror! with
            {
                Origin = new ModifierVector(plane.OriginX, plane.OriginY, plane.OriginZ),
                Normal = new ModifierVector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z)
            };
            if (!draft.SetMirrorSettings(mirror.Id, settings, out error)) return false;
            var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
            _ = TreeStateCodec.Encode(after);
            if (doc.UndoRecordingEnabled && !doc.UndoRecordingIsActive)
            {
                undo = doc.BeginUndoRecord(nodeId.HasValue ? "Set Mirror plane" : "Add Mirror");
                if (undo == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
            }
            if (oldObject is not null)
            {
                original = oldObject.Geometry.Duplicate();
                if (!doc.Objects.Replace(objectId, geometry)) throw new InvalidOperationException("Rhino could not replace BasePlane.");
                replaced = true;
            }
            else
            {
                using var attributes = new ObjectAttributes { ObjectId = objectId, Name = "BasePlane", LayerIndex = doc.Layers.CurrentLayerIndex };
                var created = doc.Objects.AddBrep(geometry, attributes);
                if (created == Guid.Empty) throw new InvalidOperationException("Rhino could not add BasePlane.");
                added = true;
                if (created != objectId) { objectId = created; throw new InvalidOperationException("Rhino changed the BasePlane identity unexpectedly."); }
            }
            // A repeat Set Plane may change only native geometry, with no private settings difference.
            var stateChanged = TreeStateCodec.Encode(before) != TreeStateCodec.Encode(after);
            if (stateChanged && !data.Edit(nodeId.HasValue ? "Set Mirror plane" : "Add Mirror",
                (tree, visibility) => { TreeStateCodec.Apply(after, tree, visibility); return true; }, out error))
                throw new InvalidOperationException(error.Length > 0 ? error : "Could not save the Mirror plane.");
            doc.Modified = true;
            resultId = mirror.Id;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            try { if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before); }
            catch { restored = false; }
            try
            {
                if (added) restored &= doc.Objects.Delete(objectId, true);
                if (replaced && original is not null) restored &= doc.Objects.Replace(objectId, original, true);
            }
            catch { restored = false; }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Use Undo to restore this action.");
            return false;
        }
        finally { original?.Dispose(); if (undo != 0) doc.EndUndoRecord(undo); }
    }
}
