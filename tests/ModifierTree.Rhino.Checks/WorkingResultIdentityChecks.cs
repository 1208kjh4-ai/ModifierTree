using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class WorkingResultIdentityChecks
{
    public static void Run()
    {
        ReassignedNativeIdentity();
        SimulatedHistoryResurrection();
        ReassignedGuidOnHistoricalUndelete();
    }

    private static void ReassignedNativeIdentity()
    {
        using var f = new Fixture();
        var undoSerial = f.Doc.NextUndoRecordSerialNumber;
        f.Sync();
        var actual = f.Working.ObjectForNode(f.Array)!.Value;
        Check(actual == f.Created.Single() && actual != f.Requested.Single() && actual != f.Source &&
              f.Working.IsProxy(actual) && f.Working.NodeForObject(actual) == f.Array && !f.Working.IsProxy(f.Source),
            "Rhino's reassigned result GUID becomes the owned native target without claiming the colliding original object");
        for (var repeat = 0; repeat < 5; repeat++) f.Sync();
        Check(f.Created.Count == 1 && f.LiveMarkedResults().SequenceEqual([actual]) &&
              f.Working.ObjectIds.SequenceEqual([actual]) && f.Doc.Objects.FindId(f.Source) is { IsHidden: true },
            "Repeated synchronization after native GUID reassignment keeps exactly one result and keeps its source hidden");
        f.Doc.Objects.Select(actual, true, true, true);
        f.Tree.SetArraySettings(f.Array, new ArraySettings { CountX = 3, Spacing = new ModifierVector(15, 10, 10) }, out _);
        f.Evaluate(); f.Sync();
        Check(f.Working.ObjectForNode(f.Array) == actual && f.Created.Count == 1 &&
              f.Doc.Objects.FindId(actual) is { } result && result.IsSelected(false) > 0 &&
              result.Geometry.GetBoundingBox(true).Max.X > 30 && f.LiveMarkedResults().Length == 1,
            "Recalculation replaces the reassigned native identity in place, retaining selection without another overlapping result");
        f.Working.Suspend();
        f.Working.AssertReadyForSave(f.Tree);
        Check(f.LiveMarkedResults().Length == 0 && !f.Working.ObjectIds.Any() &&
              f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.Objects.FindId(f.Unrelated) is not null &&
              !f.Doc.Modified && f.Doc.NextUndoRecordSerialNumber == undoSerial,
            "Save suspension removes a reassigned result while preserving original objects, saved state and native history");
    }

    private static void SimulatedHistoryResurrection()
    {
        using var f = new Fixture();
        f.Sync();
        var previous = f.Working.ObjectForNode(f.Array)!.Value;
        var previousSerial = f.Doc.Objects.FindId(previous)!.RuntimeSerialNumber;
        f.Working.Suspend();
        f.Sync();
        var current = f.Working.ObjectForNode(f.Array)!.Value;
        Check(current != previous && f.Created.Count == 2 && f.Working.IsProxy(previous) && f.Working.IsProxy(current),
            "After a replacement native identity is created, ownership retains both current and historical result GUIDs");

        // This deliberately simulates the object-table effect of Undo. It does not
        // claim to run the interactive Rhino Undo/Redo command in a headless document.
        RestoreNativeObject(f.Doc, previousSerial, previous);
        Check(f.LiveMarkedResults().Length == 2 && f.Doc.Objects.FindId(previous) is not null &&
              f.Doc.Objects.FindId(current) is not null,
            "Simulated Undo-style native undelete reproduces two overlapping result identities before reconciliation");
        f.Working.DetachCopiedMarker(previous);
        Check(f.Doc.Objects.FindId(previous)!.Attributes.GetUserString(WorkingResultObjects.Marker) is not null,
            "A resurrected historical result keeps its ownership marker instead of being mistaken for an ordinary copy");

        var currentObject = f.Doc.Objects.FindId(current)!;
        using var copyAttributes = currentObject.Attributes.Duplicate();
        copyAttributes.ObjectId = Guid.NewGuid();
        var copy = f.Doc.Objects.AddBrep((Brep)currentObject.Geometry, copyAttributes);
        f.Working.DetachCopiedMarker(copy);
        Check(copy != Guid.Empty && !f.Working.IsProxy(copy) &&
              f.Doc.Objects.FindId(copy)!.Attributes.GetUserString(WorkingResultObjects.Marker) is null,
            "An intentional native copy remains an independent fixed Brep while historical result identities stay owned");

        f.Sync();
        Check(f.LiveMarkedResults().Length == 1 && f.Working.ObjectForNode(f.Array) == current &&
              f.Doc.Objects.FindId(previous) is null && f.Doc.Objects.FindId(current) is not null &&
              f.Doc.Objects.FindId(copy) is not null && f.Doc.Objects.FindId(f.Source) is { IsHidden: true },
            "Reconciliation removes a resurrected historical duplicate and preserves the current result, fixed copy and hidden source");

        f.Working.Suspend();
        RestoreNativeObject(f.Doc, previousSerial, previous);
        var saveRejected = false;
        try { f.Working.AssertReadyForSave(f.Tree); }
        catch (InvalidOperationException) { saveRejected = true; }
        Check(saveRejected && f.Doc.Objects.FindId(current) is null && f.Doc.Objects.FindId(f.Source) is { IsHidden: false },
            "The save guard catches a lone historical result even after the current result is removed and original modes are restored");
        f.Doc.Objects.Lock(previous, true);
        f.Working.Suspend();
        f.Working.AssertReadyForSave(f.Tree);
        Check(f.LiveMarkedResults().Length == 0 && !f.Working.ObjectIds.Any() &&
              f.Created.All(id => f.Doc.Objects.FindId(id) is null),
            "Suspending before another synchronization removes every live generated identity, including a locked historical result");
        Check(f.Doc.Objects.FindId(copy) is { IsHidden: false } &&
              f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.Objects.FindId(f.Unrelated) is not null,
            "Historical duplicate cleanup preserves intentional fixed copies and restores original geometry for saving");
    }

    private static void RestoreNativeObject(RhinoDoc document, uint serial, Guid expectedId)
    {
        if (!document.Objects.Undelete(serial) || document.Objects.FindId(expectedId) is null)
            throw new InvalidOperationException("The headless native fixture could not simulate historical result resurrection by undeleting its original runtime serial.");
    }

    private static void ReassignedGuidOnHistoricalUndelete()
    {
        using var f = new Fixture(reassignNativeIds: false);
        f.Sync();
        var current = f.Working.ObjectForNode(f.Array)!.Value;
        var originalSerial = f.Doc.Objects.FindId(current)!.RuntimeSerialNumber;
        var record = f.Doc.BeginUndoRecord("Native move before working result regeneration");
        if (record == 0) throw new InvalidOperationException("Cannot begin the native move history fixture.");
        Guid moved;
        try { moved = f.Doc.Objects.Transform(current, Transform.Translation(5, 0, 0), true); }
        finally { f.Doc.EndUndoRecord(record); }
        if (moved != current || f.Doc.Objects.FindId(current)!.RuntimeSerialNumber == originalSerial)
            throw new InvalidOperationException("The native move fixture did not retain its GUID and replace its runtime object.");
        f.Working.Invalidate();
        f.Sync();

        // Reproduce Undo's native object resurrection without entering the
        // persistent UndoActive state of an interactive-command-less document.
        // The default production AddBrep path is used, with no injected GUIDs.
        if (!f.Doc.Objects.Undelete(originalSerial))
            throw new InvalidOperationException("Cannot restore the original native result after runtime cache replacement.");
        var restored = RhinoObject.FromRuntimeSerialNumber(originalSerial)!;
        var reassigned = restored.Id;
        Check(reassigned != current && !restored.IsDeleted && f.Doc.Objects.FindId(current) is not null &&
              f.LiveMarkedResults().Length == 2,
            "Native historical undelete after Move and runtime replacement reproduces a new GUID on the original runtime serial");
        Check(f.Working.IsProxy(reassigned) && f.Working.NodeForObject(reassigned) == f.Array,
            "A historical runtime serial identifies the resurrected result even when Rhino assigns it a previously unseen GUID");

        using var copyAttributes = restored.Attributes.Duplicate();
        copyAttributes.ObjectId = Guid.NewGuid();
        var copy = f.Doc.Objects.AddBrep((Brep)restored.Geometry, copyAttributes);
        var copyWasIndependent = copy != Guid.Empty && !f.Working.IsProxy(copy);
        f.Working.DetachCopiedMarker(copy);
        Check(copyWasIndependent && f.Doc.Objects.FindId(copy)!.Attributes.GetUserString(WorkingResultObjects.Marker) is null,
            "An unrelated native copy sharing a result marker is not mistaken for a historical runtime object");

        f.Doc.Objects.UnselectAll();
        f.Doc.Objects.Select(reassigned, true, true, true);
        f.Sync();
        Check(f.Working.ObjectForNode(f.Array) == current && f.Doc.Objects.FindId(reassigned) is null &&
              f.Doc.Objects.FindId(current) is { } result && result.IsSelected(false) > 0 &&
              f.LiveMarkedResults().SequenceEqual([current]) && f.Doc.Objects.FindId(copy) is not null,
            "Serial-based reconciliation removes the renamed historical overlap and transfers its selection to the surviving result");
        f.Working.Suspend();
        f.Working.AssertReadyForSave(f.Tree);
        Check(f.LiveMarkedResults().Length == 0 && !f.Working.ObjectIds.Any() &&
              f.Doc.Objects.FindId(copy) is not null && f.Doc.Objects.FindId(f.Source) is { IsHidden: false } &&
              f.Doc.Objects.FindId(f.Unrelated) is not null,
            "Saving after runtime-serial reconciliation removes generated geometry and preserves the original and ordinary copy");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Doc { get; } = RhinoDoc.CreateHeadless(null);
        public ModifierTreeModel Tree { get; } = new();
        public TreeEvaluator Evaluator { get; } = new();
        public WorkingResultObjects Working { get; }
        public Guid Source { get; }
        public Guid Unrelated { get; }
        public Guid Array { get; }
        public List<Guid> Requested { get; } = [];
        public List<Guid> Created { get; } = [];

        public Fixture(bool reassignNativeIds = true)
        {
            Doc.UndoRecordingEnabled = true;
            Doc.ModelAbsoluteTolerance = 0.001;
            using var box = new Box(Plane.WorldXY, new Interval(1, 3), new Interval(1, 3), new Interval(1, 3)).ToBrep();
            Source = Doc.Objects.AddBrep(box);
            Unrelated = Doc.Objects.AddPoint(new Point3d(50, 50, 50));
            Tree.RegisterSource(Source);
            Array = Tree.AddModifier(TreeNodeKind.Array).Id;
            Tree.Move(Tree.FindSource(Source)!.Id, Array, 0, out _);
            if (!reassignNativeIds) Working = new(Doc);
            else Working = new(Doc, addResult: (brep, attributes) =>
            {
                Requested.Add(attributes.ObjectId);
                using var collision = attributes.Duplicate();
                // A real Rhino AddBrep call must reassign this GUID because the
                // source is already using it. No fake object-table result is returned.
                collision.ObjectId = Source;
                var actual = Doc.Objects.AddBrep(brep, collision);
                Created.Add(actual);
                return actual;
            });
            Evaluate();
            Doc.Modified = false;
        }

        public Guid[] LiveMarkedResults() => Doc.Objects.GetObjectList(new ObjectEnumeratorSettings
        {
            NormalObjects = true, LockedObjects = true, HiddenObjects = true, DeletedObjects = false
        }).Where(obj => obj.Attributes.GetUserString(WorkingResultObjects.Marker) is not null).Select(obj => obj.Id).ToArray();
        public void Evaluate() => Evaluator.Rebuild(Tree, id => Doc.Objects.FindId(id)?.Geometry, Doc.ModelAbsoluteTolerance);
        public void Sync() => Working.Synchronize(Tree, Evaluator, null, true);
        public void Dispose() { Working.Dispose(); Evaluator.Dispose(); Doc.Dispose(); }
    }
}
