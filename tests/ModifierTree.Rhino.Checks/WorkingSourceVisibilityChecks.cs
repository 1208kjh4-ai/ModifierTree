using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class WorkingSourceVisibilityChecks
{
    public static void Run()
    {
        OwnershipAndModes();
        AttributeAndGeometryChanges();
        LayerModes();
        RuntimeScope();
        PreserveNativeUndo();
    }

    private static void OwnershipAndModes()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        var normal = doc.Objects.AddPoint(Point3d.Origin);
        var locked = doc.Objects.AddPoint(new Point3d(1, 0, 0));
        var hidden = doc.Objects.AddPoint(new Point3d(2, 0, 0));
        doc.Objects.Lock(locked, true);
        doc.Objects.Hide(hidden, true);
        var visibility = new WorkingSourceVisibility(doc);
        doc.Modified = false;
        var serial = doc.NextUndoRecordSerialNumber;
        visibility.Apply([normal, locked, hidden, normal, Guid.Empty]);
        Check(doc.Objects.FindId(normal)!.IsHidden && doc.Objects.FindId(locked)!.IsHidden,
            "Working preview hides both normal and locked original inputs from native snapping");
        Check(visibility.IsManagedHidden(normal) && visibility.IsManagedHidden(locked) && !visibility.IsManagedHidden(hidden),
            "Working visibility owns only inputs it hid, preserving pre-existing user hides");
        Check(!visibility.IsLogicallyLocked(normal) && visibility.IsLogicallyLocked(locked),
            "A temporarily hidden locked input retains its logical lock");
        Check(!doc.Modified && doc.UndoRecordingEnabled && doc.NextUndoRecordSerialNumber == serial && !visibility.IsBusy,
            "Working-source suppression neither dirties the model nor opens an Undo record");
        visibility.Apply([locked, hidden]);
        Check(doc.Objects.FindId(normal)!.Attributes.Mode == ObjectMode.Normal && !visibility.IsManagedHidden(normal) &&
            visibility.IsManagedHidden(locked), "Changing scope restores only inputs no longer suppressed");
        visibility.RestoreAll();
        Check(doc.Objects.FindId(locked)!.Attributes.Mode == ObjectMode.Locked && doc.Objects.FindId(hidden)!.IsHidden &&
            !visibility.IsManagedHidden(locked), "Restoring visibility preserves the original Locked and user Hidden modes");
        Check(!doc.Modified && doc.NextUndoRecordSerialNumber == serial,
            "Restoring source modes adds no unsaved change or native Undo record");
    }

    private static void AttributeAndGeometryChanges()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        using var box = new BoundingBox(0, 0, 0, 2, 2, 2).ToBrep();
        var id = doc.Objects.AddBrep(box);
        var visibility = new WorkingSourceVisibility(doc);
        visibility.Apply([id]);
        using (var attributes = doc.Objects.FindId(id)!.Attributes.Duplicate())
        {
            attributes.Name = "Edited while suppressed";
            attributes.LayerIndex = doc.Layers.Add("Edited layer", System.Drawing.Color.Blue);
            Check(documentChange: doc.Objects.ModifyAttributes(id, attributes, true),
                message: "Native source attributes remain editable while working visibility owns the mode");
        }
        var layerIndex = doc.Objects.FindId(id)!.Attributes.LayerIndex;
        using var moved = new BoundingBox(10, 0, 0, 12, 2, 2).ToBrep();
        Check(doc.Objects.Replace(id, (GeometryBase)moved, true) && visibility.IsManagedHidden(id),
            "Replacing hidden native geometry keeps working visibility ownership on the original GUID");
        visibility.RestoreAll();
        var obj = doc.Objects.FindId(id)!;
        Check(obj.Attributes.Mode == ObjectMode.Normal && obj.Attributes.Name == "Edited while suppressed" &&
            obj.Attributes.LayerIndex == layerIndex && obj.Geometry.GetBoundingBox(true).Min.X == 10,
            "Restoring only source mode preserves later rename, layer and geometry changes");
        visibility.Apply([id]);
        Check(doc.Objects.Delete(doc.Objects.FindId(id)!, true, true), "A suppressed source can be deleted by its owning document operation");
        visibility.RestoreAll();
        Check(!visibility.IsManagedHidden(id) && !visibility.IsLogicallyLocked(id) && !visibility.IsBusy,
            "Restoring after native deletion forgets missing source ownership safely");
    }

    private static void LayerModes()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        var layerIndex = doc.Layers.Add("Locked source layer", System.Drawing.Color.Red);
        using var attributes = new ObjectAttributes { LayerIndex = layerIndex };
        var id = doc.Objects.AddPoint(Point3d.Origin, attributes);
        var layer = doc.Layers.FindIndex(layerIndex)!;
        layer.IsLocked = true;
        var visibility = new WorkingSourceVisibility(doc);
        Check(visibility.IsLogicallyLocked(id), "Working-source logical editability respects a locked Rhino layer");
        visibility.Apply([id]);
        Check(visibility.IsManagedHidden(id) && visibility.IsLogicallyLocked(id),
            "A source on a locked layer is suppressed without losing its layer lock");
        visibility.RestoreAll();
        Check(doc.Objects.FindId(id)!.Attributes.Mode == ObjectMode.Normal && doc.Layers.FindIndex(layerIndex)!.IsLocked,
            "Scope restoration changes object mode without unlocking its Rhino layer");
    }

    private static void RuntimeScope()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        doc.Modified = false;
        using (new RuntimeDocumentEdit(doc))
        {
            Check(!doc.UndoRecordingEnabled, "Derived native edits temporarily disable Undo recording");
            doc.Modified = true;
            using (new RuntimeDocumentEdit(doc)) doc.Modified = false;
            Check(!doc.UndoRecordingEnabled && doc.Modified,
                "Nested runtime edits restore the outer edit's Undo and modified state");
        }
        Check(doc.UndoRecordingEnabled && !doc.Modified,
            "The outer runtime edit restores the original native history and saved state");
        doc.Modified = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(doc);
            doc.Modified = false;
            throw new InvalidOperationException("Expected runtime-edit check");
        }
        catch (InvalidOperationException) { }
        Check(doc.Modified && doc.UndoRecordingEnabled,
            "Runtime-edit disposal preserves real unsaved changes even when a derived edit fails");
    }

    private static void PreserveNativeUndo()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        var id = doc.Objects.AddPoint(Point3d.Origin);
        var undo = doc.BeginUndoRecord("Source move before preview synchronization");
        Check(undo != 0, "Native-history visibility fixture opens an ordinary command Undo record");
        doc.Objects.Transform(id, Transform.Translation(5, 0, 0), true);
        doc.EndUndoRecord(undo);
        var visibility = new WorkingSourceVisibility(doc);
        visibility.Apply([id]);
        visibility.RestoreAll();
        Check(doc.Undo() && doc.Objects.FindId(id)!.Geometry.GetBoundingBox(true).Min.X == 0,
            "Preview hide and restore preserve the preceding real native geometry Undo");
    }

    private static void Check(bool documentChange, string message)
    {
        if (!documentChange) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
