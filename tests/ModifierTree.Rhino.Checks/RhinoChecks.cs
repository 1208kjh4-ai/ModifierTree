using ModifierTree.Rhino;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Geometry;

internal static class RhinoChecks
{
    public static void Run()
    {
        BendControlCageChecks.Run();
        BendControlCageCacheChecks.Run();
        BendGeometryChecks.Run();
        BendDocumentChecks.Run();
        BendWorkingChecks.Run();
        GeometryChecks();
        TreeEvaluationChecks.Run();
        BooleanKindChecks.Run();
        BooleanUnionEdgeChecks.Run();
        ModifierOperationChecks.Run();
        ResultGeometryChecks.Run();
        LiveTreePreviewChecks.Run();
        LiveOptimizationChecks.Run();
        TreeArchiveChecks.Run();
        TreeUndoChecks.Run();
        BatchMoveUndoChecks.Run();
        ModifierDocumentOperationChecks.Run();
        ControlDocumentChecks.Run();
        WorkingSourceVisibilityChecks.Run();
        WorkingResultTransformChecks.Run();
        WorkingMultiPreviewChecks.Run();
        WorkingResultObjectsChecks.Run();
        WorkingResultIdentityChecks.Run();
        ResultCommitChecks.Run();
        SubtreeTransformChecks.Run();
        ViewportSelectionChecks.Run();
        SelectionInteractionChecks.Run();
        PreviewAppearanceChecks.Run();
        SourceNameEditorChecks.Run();
        DocumentSnapshotChecks();
        ManagerLayoutChecks.Run();
        TreeGridSelectionChecks.Run();
        Console.WriteLine("All Rhino geometry and document snapshot checks passed. Interactive event scheduling, rendering and picking require UI verification.");
    }

    private static Box Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(Plane.WorldXY, new Interval(x0, x1), new Interval(y0, y1), new Interval(z0, z1));

    private static void GeometryChecks()
    {
        using var first = Extrusion.CreateBoxExtrusion(Box(0, 0, 0, 10, 10, 10), true);
        using var cutter = Extrusion.CreateBoxExtrusion(Box(5, -1, -1, 15, 11, 11), true);
        using var state = new DifferenceFixture(Guid.NewGuid(), Guid.NewGuid());
        state.Rebuild(first, cutter, 0.001);
        Volume(state, 500, "Overlapping closed extrusions produce A-B");
        using var firstBrep = first.ToBrep();
        using var cutterBrep = cutter.ToBrep();
        firstBrep.Flip();
        state.Rebuild(firstBrep, cutterBrep, 0.001);
        Volume(state, 500, "Inward source orientation is normalized on a copy");
        Check(firstBrep.SolidOrientation == BrepSolidOrientation.Inward, "Source Brep orientation stays unchanged");

        using var disjoint = Box(20, 20, 20, 30, 30, 30).ToBrep();
        state.Rebuild(first, disjoint, 0.001);
        Volume(state, 1000, "Disjoint cutter keeps all of A");
        using var slab = Box(4, -1, -1, 6, 11, 11).ToBrep();
        state.Rebuild(first, slab, 0.001);
        Volume(state, 800, "Splitting cutter preserves both result pieces");
        Check(state.Results.Count == 2, "Multiple Boolean results are retained");

        using var contains = Box(-1, -1, -1, 11, 11, 11).ToBrep();
        state.Rebuild(first, contains, 0.001);
        Check(state.Error is null && state.HasValidResult && state.Results.Count == 0, "Fully subtracted A becomes a valid empty result");
        state.Rebuild(first, cutter, 0.001);
        using var invalidType = new LineCurve(Point3d.Origin, new Point3d(1, 0, 0));
        state.Rebuild(first, invalidType, 0.001);
        Check(state.Error is not null && state.HasValidResult, "Invalid input is reported while keeping the last valid result");
        Check(Math.Abs(GetVolume(state) - 500) < 0.01, "Failure does not erase the cached result");
        state.Rebuild(first, cutter, 0.001);
        Volume(state, 500, "Correcting the input recovers from failure");
        state.SwapInputs();
        Check(!state.HasValidResult, "Swapping inputs invalidates the previous expression's result");
        state.Rebuild(cutter, first, 0.001);
        Volume(state, 940, "Swapping A/B changes the difference");
    }

    private static void DocumentSnapshotChecks()
    {
        var supportsRedo = BaselineRedo();
        // The windowless host does not emit interactive document events. Invoke the same
        // production evaluator explicitly against current document geometry; do not claim
        // that this verifies the UI/Idle event path. RebuildGate checks cover event coalescing.
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.ModelAbsoluteTolerance = 0.001;
        doc.UndoRecordingEnabled = true;
        using var first = Extrusion.CreateBoxExtrusion(Box(0, 0, 0, 10, 10, 10), true);
        using var cutter = Extrusion.CreateBoxExtrusion(Box(5, -1, -1, 15, 11, 11), true);
        var a = doc.Objects.AddExtrusion(first);
        var b = doc.Objects.AddExtrusion(cutter);
        using var state = new DifferenceFixture(a, b);
        void Rebuild() => state.Rebuild(doc.Objects.FindId(a)?.Geometry, doc.Objects.FindId(b)?.Geometry, doc.ModelAbsoluteTolerance);
        Rebuild();
        Volume(state, 500, "Document inputs are evaluated by GUID");
        doc.Modified = false;
        Rebuild();
        Check(!doc.Modified, "Preview rebuild does not mark the document as modified");

        var record = doc.BeginUndoRecord("Move cutter for Difference check");
        Check(record != 0, "Source transform opens an Undo record");
        Check(doc.Objects.Transform(b, Transform.Translation(20, 0, 0), true) == b, "Source transform retains its GUID");
        doc.EndUndoRecord(record);
        Rebuild();
        Volume(state, 1000, "Rebuild reads the moved document source");
        Check(doc.Objects.Count == 2, "Preview rebuild adds no generated document objects");
        Check(doc.Undo(), "Original source edit remains undoable");
        Rebuild();
        Volume(state, 500, "Rebuild after Undo restores the corresponding result");
        if (!supportsRedo)
        {
            Console.WriteLine("SKIP: Redo/document-deletion replay needs interactive Rhino; the direct Redo API also failed in a baseline without ModifierTree.");
            return;
        }
        Check(doc.Redo(), "Preview rebuild preserves the source edit's Redo record");
        Rebuild();
        Volume(state, 1000, "Rebuild after Redo restores the changed result");

        record = doc.BeginUndoRecord("Delete cutter for Difference check");
        Check(doc.Objects.Delete(b, true), "Delete source for failure test");
        doc.EndUndoRecord(record);
        Rebuild();
        Check(state.Error is not null && state.HasValidResult, "Rebuild after source deletion marks failure and preserves last result");
        Check(doc.Undo(), "Source deletion is undoable");
        Rebuild();
        Volume(state, 1000, "Undelete restores the input connection by GUID");
        doc.Modified = false;
    }

    private static bool BaselineRedo()
    {
        using var baseline = RhinoDoc.CreateHeadless(null);
        baseline.UndoRecordingEnabled = true;
        using var box = Box(0, 0, 0, 10, 10, 10).ToBrep();
        var id = baseline.Objects.AddBrep(box);
        var record = baseline.BeginUndoRecord("Baseline transform without ModifierTree");
        baseline.Objects.Transform(id, Transform.Translation(20, 0, 0), true);
        baseline.EndUndoRecord(record);
        var undo = baseline.Undo();
        var redo = baseline.Redo();
        Console.WriteLine($"HOST BASELINE (no ModifierTree): Undo={undo}, Redo={redo}");
        return undo && redo;
    }

    private static double GetVolume(DifferenceFixture state)
    {
        var total = 0.0;
        foreach (var result in state.Results)
        {
            using var mass = VolumeMassProperties.Compute(result);
            total += mass.Volume;
        }
        return total;
    }

    private static void Volume(DifferenceFixture state, double expected, string description)
    {
        Check(state.Error is null && state.HasValidResult && Math.Abs(GetVolume(state) - expected) < 0.01,
            $"{description} (expected volume {expected}, got {GetVolume(state)}; error={state.Error})");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
