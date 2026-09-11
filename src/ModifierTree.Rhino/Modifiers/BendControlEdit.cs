using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Owns native Control Box geometry and tree settings in one Rhino Undo transaction.</summary>
internal static class BendControlEdit
{
    public static bool Add(RhinoDoc doc, TreeDocumentData data, Guid bendId, out Guid controlId, out string error)
    {
        controlId = Guid.Empty;
        if (!CanEdit(doc, data, out error)) return false;
        if (data.Tree.Find(bendId) is not { Kind: TreeNodeKind.Bend })
        { error = "Select a Bend Modifier."; return false; }
        var before = data.Capture();
        var wasModified = doc.Modified;
        var added = Guid.Empty;
        uint undo = 0;
        try
        {
            var box = FitBox(doc, data.Tree, bendId, Plane.WorldXY, allowEmpty: true);
            using var geometry = ControlBoxGeometry.Create(box);
            var draft = new ModifierTreeModel();
            var wires = new InputVisibility();
            TreeStateCodec.Apply(before, draft, wires);
            var requested = Guid.NewGuid();
            if (!draft.AddControlBox(bendId, requested, out error)) return false;
            var preparedId = draft.FindSource(requested)!.Id;
            var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
            _ = TreeStateCodec.Encode(after);
            var ordinal = draft.Find(bendId)!.Children.Count(id => draft.Find(id)!.Kind == TreeNodeKind.ControlBox);
            using var attributes = new ObjectAttributes
            {
                ObjectId = requested,
                Name = ordinal == 1 ? "Control Box" : $"Control Box {ordinal}",
                LayerIndex = doc.Layers.CurrentLayerIndex
            };
            undo = BeginUndo(doc, "Add Bend Control Box");
            added = doc.Objects.AddBrep(geometry, attributes);
            if (added == Guid.Empty) throw new InvalidOperationException("Rhino could not add the Control Box.");
            if (added != requested) throw new InvalidOperationException("Rhino changed the Control Box identity unexpectedly.");
            if (!data.Edit("Add Bend Control Box", (tree, visibility) =>
                { TreeStateCodec.Apply(after, tree, visibility); return true; }, out error))
                throw new InvalidOperationException(error.Length > 0 ? error : "The Control Box could not be registered.");
            controlId = preparedId;
            doc.Modified = true;
            return true;
        }
        catch (Exception exception)
        {
            var restored = RestoreTree(data, before);
            try { if (added != Guid.Empty) restored &= doc.Objects.Delete(added, true); }
            catch { restored = false; }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Use Undo to restore this action.");
            return false;
        }
        finally { if (undo != 0) doc.EndUndoRecord(undo); }
    }

    public static bool Fit(RhinoDoc doc, TreeDocumentData data, Guid controlId, out string error)
    {
        if (!CanEdit(doc, data, out error)) return false;
        if (data.Tree.Find(controlId) is not { Kind: TreeNodeKind.ControlBox, ObjectId: { } objectId, ParentId: { } bendId })
        { error = "Select a Control Box."; return false; }
        var obj = doc.Objects.FindId(objectId);
        if (obj is null || obj.IsLocked || obj.IsReference || obj.GripsOn || obj.IsHidden || LayerLocked(doc, obj.Attributes.LayerIndex))
        { error = "Make the Control Box visible and editable before fitting it."; return false; }
        var wasModified = doc.Modified;
        GeometryBase? original = null;
        var replaced = false;
        uint undo = 0;
        try
        {
            if (!ControlBoxGeometry.TryGetBox(obj.Geometry, doc.ModelAbsoluteTolerance, out var oldBox, out error)) return false;
            var box = FitBox(doc, data.Tree, bendId, oldBox.Plane, allowEmpty: false);
            using var geometry = ControlBoxGeometry.Create(box);
            if (GeometryBase.GeometryEquals(obj.Geometry, geometry)) return true;
            original = obj.Geometry.Duplicate();
            undo = BeginUndo(doc, "Fit Bend Control Box to inputs");
            replaced = true; // A native notification can throw after Rhino has already replaced the object.
            if (!doc.Objects.Replace(objectId, (GeometryBase)geometry, true))
                throw new InvalidOperationException("Rhino could not resize the Control Box.");
            doc.Modified = true;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            try { if (replaced && original is not null) restored = doc.Objects.Replace(objectId, original, true); }
            catch { restored = false; }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Use Undo to restore this action.");
            return false;
        }
        finally { original?.Dispose(); if (undo != 0) doc.EndUndoRecord(undo); }
    }

    public static bool SetProperties(RhinoDoc doc, TreeDocumentData data, Guid controlId, string name,
        ControlBoxSettings settings, out string error)
    {
        if (!CanEdit(doc, data, out error)) return false;
        if (data.Tree.Find(controlId) is not { Kind: TreeNodeKind.ControlBox, ObjectId: { } objectId })
        { error = "Select a Control Box."; return false; }
        if (!ModifierNames.TryNormalize(name, out var normalized, out error)) return false;
        var obj = doc.Objects.FindId(objectId);
        if (obj is null || obj.IsLocked || obj.IsReference || obj.GripsOn || LayerLocked(doc, obj.Attributes.LayerIndex))
        { error = "The Control Box is missing, locked, referenced, or has control points on."; return false; }
        var before = data.Capture();
        var wasModified = doc.Modified;
        using var original = obj.Attributes.Duplicate();
        var renamed = false;
        uint undo = 0;
        try
        {
            var draft = new ModifierTreeModel();
            var wires = new InputVisibility();
            TreeStateCodec.Apply(before, draft, wires);
            if (!draft.SetControlBoxSettings(controlId, settings, out error)) return false;
            var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
            var settingsChanged = TreeStateCodec.Encode(before) != TreeStateCodec.Encode(after);
            var nameChanged = original.Name != normalized;
            if (!settingsChanged && !nameChanged) return true;
            if (settingsChanged && draft.Find(controlId)!.ParentId is { } bendId && draft.Find(bendId)!.Enabled)
            {
                using var validation = new TreeEvaluator();
                validation.Rebuild(draft, id => doc.Objects.FindId(id)?.Geometry, doc.ModelAbsoluteTolerance);
                if (validation.Find(bendId) is { Status: "FAILED" } failed)
                    throw new InvalidOperationException(failed.Error ?? "These settings cannot produce a valid Bend result.");
            }
            undo = BeginUndo(doc, "Edit Bend Control Box");
            if (nameChanged)
            {
                using var attributes = original.Duplicate();
                attributes.Name = normalized;
                renamed = true;
                if (!doc.Objects.ModifyAttributes(objectId, attributes, true))
                    throw new InvalidOperationException("Rhino could not rename the Control Box.");
            }
            if (settingsChanged && !data.Edit("Edit Bend Control Box", (tree, visibility) =>
                { TreeStateCodec.Apply(after, tree, visibility); return true; }, out error))
                throw new InvalidOperationException(error.Length > 0 ? error : "The Control Box settings could not be saved.");
            doc.Modified = true;
            return true;
        }
        catch (Exception exception)
        {
            var restored = RestoreTree(data, before);
            try { if (renamed) restored &= doc.Objects.ModifyAttributes(objectId, original, true); }
            catch { restored = false; }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Use Undo to restore this action.");
            return false;
        }
        finally { if (undo != 0) doc.EndUndoRecord(undo); }
    }

    // Fit to actual immediate input results, including nested Modifiers, rather than
    // their hidden raw cutters or previously bent outputs. Preserve the selected axes.
    private static Box FitBox(RhinoDoc doc, ModifierTreeModel tree, Guid bendId, Plane frame, bool allowEmpty)
    {
        using var evaluator = new TreeEvaluator();
        evaluator.Rebuild(tree, id => doc.Objects.FindId(id)?.Geometry, doc.ModelAbsoluteTolerance);
        var bounds = BoundingBox.Empty;
        var toLocal = Transform.PlaneToPlane(frame, Plane.WorldXY);
        foreach (var id in tree.Find(bendId)!.Children.Where(id => !tree.Find(id)!.IsControl))
        {
            var input = evaluator.Find(id);
            if (input is not { IsCurrent: true })
                throw new InvalidOperationException("Restore valid Bend inputs before fitting a Control Box.");
            foreach (var brep in input.Results) bounds.Union(brep.GetBoundingBox(toLocal));
        }
        if (!bounds.IsValid)
        {
            if (!allowEmpty) throw new InvalidOperationException("Add a nonempty geometry input before fitting the Control Box.");
            bounds = new BoundingBox(-5, -5, -5, 5, 5, 5);
        }
        var minimum = Math.Max(doc.ModelAbsoluteTolerance * 20, 0.001);
        Interval Extent(double a, double b) => b - a >= minimum ? new Interval(a, b) :
            new Interval((a + b - minimum) / 2, (a + b + minimum) / 2);
        return new Box(frame, Extent(bounds.Min.X, bounds.Max.X), Extent(bounds.Min.Y, bounds.Max.Y), Extent(bounds.Min.Z, bounds.Max.Z));
    }

    private static bool CanEdit(RhinoDoc doc, TreeDocumentData data, out string error)
    {
        error = data.ReadOnlyReason ?? "";
        if (error.Length > 0) return false;
        if (doc.UndoActive || doc.RedoActive) { error = "Wait for Undo or Redo to finish."; return false; }
        if (!double.IsFinite(doc.ModelAbsoluteTolerance) || doc.ModelAbsoluteTolerance <= 0)
        { error = "The document tolerance must be positive and finite."; return false; }
        return true;
    }

    private static uint BeginUndo(RhinoDoc doc, string description)
    {
        if (!doc.UndoRecordingEnabled || doc.UndoRecordingIsActive) return 0;
        var undo = doc.BeginUndoRecord(description);
        if (undo == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
        return undo;
    }

    private static bool LayerLocked(RhinoDoc doc, int index)
    {
        if (index < 0 || index >= doc.Layers.Count) return true;
        var layer = doc.Layers.FindIndex(index);
        var visited = new HashSet<Guid>();
        while (layer is not null)
        {
            if (!visited.Add(layer.Id) || layer.IsLocked) return true;
            if (layer.ParentLayerId == Guid.Empty) return false;
            layer = doc.Layers.FindId(layer.ParentLayerId);
            if (layer is null) return true;
        }
        return false;
    }

    private static bool RestoreTree(TreeDocumentData data, ModifierDocumentState before)
    {
        try { if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before); return true; }
        catch { return false; }
    }
}
