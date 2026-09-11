using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class WorkingResultObjectsChecks
{
    public static void Run()
    {
        NativeTargets();
        ScopeAndVisibility();
        SuspendAndCopy();
        SaveAndExportGuards();
        HiddenResult();
        InvalidResult();
    }

    private static void NativeTargets()
    {
        using var f = new Fixture();
        var serial = f.Doc.NextUndoRecordSerialNumber;
        f.Sync();
        var proxy = f.Working.ObjectForNode(f.Array)!.Value;
        var obj = f.Doc.Objects.FindId(proxy)!;
        Check(obj.Geometry is Brep brep && brep.IsSolid && brep.GetBoundingBox(true).Max.X > 10 && obj.IsSelectable(),
            "The complete final result is a selectable native Brep with all generated edges and faces available to Rhino snaps");
        Check(f.Doc.Objects.FindId(f.Source) is { IsHidden: true } source && !source.IsSelectable() &&
              f.Doc.Objects.FindId(f.Unrelated) is { IsHidden: false },
            "Only owned inputs are truly hidden and unselectable; unrelated Rhino objects remain available");
        Check(!f.Doc.Modified && f.Doc.NextUndoRecordSerialNumber == serial && f.Tree.FindSource(proxy) is null && f.Tree.SourceCount == 1,
            "Creating working geometry neither dirties the document, adds Undo history, nor registers generated sources");
        var runtime = obj.RuntimeSerialNumber;
        f.Sync();
        Check(f.Doc.Objects.FindId(proxy)!.RuntimeSerialNumber == runtime,
            "An unchanged working display does not replace its native result or churn selection and snap geometry");
        f.Doc.Objects.Select(proxy, true, true, true);
        f.Tree.SetArraySettings(f.Array, new ArraySettings { CountX = 3, Spacing = new ModifierVector(15, 10, 10) }, out _);
        f.Evaluate(); f.Sync();
        Check(f.Working.ObjectForNode(f.Array) == proxy && f.Doc.Objects.FindId(proxy)!.IsSelected(false) > 0 &&
              f.Doc.Objects.FindId(proxy)!.Geometry.GetBoundingBox(true).Max.X > 30,
            "Recalculation updates the same selected working identity to the new complete result");
        f.Working.ClearPendingSelection();
    }

    private static void ScopeAndVisibility()
    {
        using var f = new Fixture();
        var parent = f.Tree.AddModifier(TreeNodeKind.Mirror);
        f.Tree.SetMirrorSettings(parent.Id, MirrorSettings.Default with { Union = false }, out _);
        f.Tree.Move(f.Array, parent.Id, 0, out _);
        using var surface = new PlaneSurface(Plane.WorldYZ, new Interval(-5, 5), new Interval(-5, 5));
        using var plane = surface.ToBrep();
        var control = f.Doc.Objects.AddBrep(plane);
        f.Tree.SetBasePlane(parent.Id, control, out _);
        f.Evaluate(); f.Sync();
        Check(f.Working.ObjectForNode(parent.Id).HasValue && !f.Working.ObjectForNode(f.Array).HasValue &&
              f.Doc.Objects.FindId(control)!.IsHidden && f.Doc.Objects.FindId(f.Source)!.IsHidden,
            "Normal mode exposes only the final root result and hides nested inputs and BasePlane from native picking");
        f.Working.Synchronize(f.Tree, f.Evaluator, parent.Id, true);
        Check(f.Working.ObjectForNode(f.Array).HasValue && f.Doc.Objects.FindId(control) is { IsHidden: false } &&
              f.Doc.Objects.FindId(f.Source)!.IsHidden,
            "Entering a Mirror exposes its immediate BasePlane and child result while keeping deeper raw geometry hidden");
        f.Working.Synchronize(f.Tree, f.Evaluator, f.Array, true);
        Check(f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.Objects.FindId(control)!.IsHidden &&
              !f.Working.ObjectForNode(f.Array).HasValue,
            "Entering the nested Array exposes its input and excludes the higher-level BasePlane and intermediate result");
        f.Working.Synchronize(f.Tree, f.Evaluator, null, true);
        Check(f.Doc.Objects.FindId(f.Source)!.IsHidden && f.Doc.Objects.FindId(control)!.IsHidden,
            "Leaving input editing removes original objects from native selection again");
        f.Working.Synchronize(f.Tree, f.Evaluator, null, false);
        Check(f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.Objects.FindId(control) is { IsHidden: false } &&
              !f.Working.ObjectIds.Any(), "Show result OFF restores original visibility and removes all working results");
        f.Doc.Objects.Hide(f.Source, true);
        f.Sync(); f.Working.Suspend();
        Check(f.Doc.Objects.FindId(f.Source)!.IsHidden && !f.Working.Sources.IsManagedHidden(f.Source),
            "Preview toggles never reveal an input the user had explicitly hidden");
    }

    private static void SuspendAndCopy()
    {
        using var f = new Fixture();
        f.Sync();
        var proxy = f.Working.ObjectForNode(f.Array)!.Value;
        var obj = f.Doc.Objects.FindId(proxy)!;
        using var attributes = obj.Attributes.Duplicate();
        attributes.ObjectId = Guid.NewGuid();
        var copy = f.Doc.Objects.AddBrep((Brep)obj.Geometry, attributes);
        f.Working.DetachCopiedMarker(copy);
        Check(f.Doc.Objects.FindId(copy)!.Attributes.GetUserString(WorkingResultObjects.Marker) is null && !f.Working.IsProxy(copy),
            "A native copy becomes an independent ordinary Brep rather than a second working-result link");
        f.Doc.Objects.Select(proxy, true, true, true);
        f.Doc.Modified = false;
        var undoSerial = f.Doc.NextUndoRecordSerialNumber;
        f.Working.Suspend();
        Check(f.Doc.Objects.FindId(proxy) is null && f.Doc.Objects.FindId(f.Source) is { IsHidden: false } &&
              f.Doc.Objects.FindId(copy) is not null && !f.Doc.Modified && f.Doc.NextUndoRecordSerialNumber == undoSerial,
            "Save suspension removes only owned temporary results, restores original modes, and keeps copies and document history intact");
        f.Sync();
        Check(f.Working.ObjectForNode(f.Array) == proxy && f.Doc.Objects.FindId(proxy)!.IsSelected(false) > 0 &&
              f.Doc.Objects.FindId(f.Source)!.IsHidden && !f.Doc.Modified,
            "After saving, working geometry and prior result selection return without dirtying the model");
        f.Working.Suspend();
        f.Tree.Move(f.Tree.FindSource(f.Source)!.Id, null, 0, out _);
        f.Evaluate(); f.Sync();
        Check(f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && !f.Working.ObjectIds.Any(),
            "Moving the input back to the tree root restores a normal Rhino object without a stale generated result");
    }

    private static void HiddenResult()
    {
        using var f = new Fixture();
        f.Sync();
        var proxy = f.Working.ObjectForNode(f.Array)!.Value;
        f.Doc.Objects.Hide(proxy, true);
        Check(!f.Working.IsVisible(f.Array),
            "A user-hidden working result is excluded from conduit rendering and custom picking");
        f.Working.Invalidate(); f.Sync();
        Check(f.Doc.Objects.FindId(proxy) is { IsHidden: true } && !f.Working.IsVisible(f.Array),
            "Recalculating a hidden working result preserves the user's Hide");
        f.Working.Suspend();
        Check(!f.Working.IsVisible(f.Array),
            "Suspending a hidden result also suppresses its retained diagnostic display");
        f.Sync();
        f.Doc.Objects.Show(proxy, true);
        Check(f.Working.IsVisible(f.Array),
            "Rhino Show restores the working result's rendering and custom picking eligibility");
    }

    private static void InvalidResult()
    {
        using var f = new Fixture();
        f.Sync();
        f.Doc.Objects.Delete(f.Doc.Objects.FindId(f.Source)!, true, true);
        f.Evaluate(); f.Sync();
        Check(!f.Working.ObjectForNode(f.Array).HasValue && f.Evaluator.Find(f.Array) is { IsCurrent: false, HasResult: true },
            "A failed operation removes stale native snap targets while the evaluator can retain its diagnostic last-valid display");
    }

    private static void SaveAndExportGuards()
    {
        using var f = new Fixture();
        f.Sync();
        var rejected = false;
        try { f.Working.AssertReadyForSave(f.Tree); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "A full document save guard rejects unsuspended working geometry and temporary source hiding");
        var proxy = f.Working.ObjectForNode(f.Array)!.Value;
        f.Working.SetExportMarkers(restore: false);
        Check(f.Doc.Objects.FindId(proxy)!.Attributes.GetUserString(WorkingResultObjects.Marker) is null && f.Working.IsProxy(proxy),
            "Export Selected writes an ordinary Brep without a disposable tree marker while preserving runtime ownership");
        f.Working.SetExportMarkers(restore: true);
        Check(f.Doc.Objects.FindId(proxy)!.Attributes.GetUserString(WorkingResultObjects.Marker) is not null,
            "Completing or cancelling export restores the working object's runtime marker");
        f.Doc.Objects.Lock(proxy, true);
        f.Working.Suspend();
        f.Working.AssertReadyForSave(f.Tree);
        Check(f.Doc.Objects.FindId(proxy) is null && !f.Doc.Objects.FindId(f.Source)!.IsHidden,
            "A locked working result can still be excluded from the saved model without changing original modes");
        f.Sync();
        Check(f.Doc.Objects.FindId(proxy) is { IsLocked: true },
            "The working result's explicit lock returns after save suspension");
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
        public Fixture()
        {
            Doc.UndoRecordingEnabled = true;
            Doc.ModelAbsoluteTolerance = 0.001;
            using var box = new Box(Plane.WorldXY, new Interval(1, 3), new Interval(1, 3), new Interval(1, 3)).ToBrep();
            Source = Doc.Objects.AddBrep(box);
            Unrelated = Doc.Objects.AddPoint(new Point3d(50, 50, 50));
            Tree.RegisterSource(Source);
            Array = Tree.AddModifier(TreeNodeKind.Array).Id;
            Tree.Move(Tree.FindSource(Source)!.Id, Array, 0, out _);
            Working = new(Doc);
            Evaluate(); Doc.Modified = false;
        }
        public void Evaluate() => Evaluator.Rebuild(Tree, id => Doc.Objects.FindId(id)?.Geometry, Doc.ModelAbsoluteTolerance);
        public void Sync() => Working.Synchronize(Tree, Evaluator, null, true);
        public void Dispose() { Working.Dispose(); Evaluator.Dispose(); Doc.Dispose(); }
    }
}
