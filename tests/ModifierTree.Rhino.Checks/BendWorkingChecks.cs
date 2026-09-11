using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.Geometry;

internal static class BendWorkingChecks
{
    public static void Run()
    {
        NativeResultAndScope();
        WholeResultTransform();
        LiveControlAndCache();
    }

    private static void NativeResultAndScope()
    {
        using var f = new Fixture();
        f.Working.Synchronize(f.Data.Tree, f.Evaluator, null, true);
        var proxy = f.Working.ObjectForNode(f.Bend);
        Check(proxy is { } && f.Doc.Objects.FindId(proxy.Value)?.Geometry is Brep { IsValid: true, IsSolid: true } &&
            f.Doc.Objects.FindId(f.Source)!.IsHidden && f.Doc.Objects.FindId(f.Box)!.IsHidden,
            "Bend provides a native solid working result while hiding input and Control Box from normal snapping");
        var id = proxy!.Value;
        var volume = Volume((Brep)f.Doc.Objects.FindId(id)!.Geometry);
        Check(Math.Abs(volume - 8000) < 0.1 && f.Doc.Objects.Select(id, true, true, true),
            "Bend working geometry excludes the control volume and supports native selection");
        f.Working.Synchronize(f.Data.Tree, f.Evaluator, f.Bend, true);
        Check(f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.Objects.FindId(f.Box) is { IsHidden: false } &&
            SubtreeSelection.Select(f.Doc, f.Data.Tree, f.Control, out _) &&
            f.Doc.Objects.GetSelectedObjects(false, false).Select(obj => obj.Id).SequenceEqual(new[] { f.Box }),
            "Entering Bend exposes its control and selects just that native Control Box for editing");
        f.Working.Synchronize(f.Data.Tree, f.Evaluator, null, true);
        Check(f.Working.ObjectIds.Count() == 1 && f.Doc.Objects.FindId(f.Box)!.IsHidden,
            "Returning from Bend edit scope keeps exactly one native result and hides the control again");
    }

    private static void WholeResultTransform()
    {
        using var f = new Fixture();
        f.Working.Synchronize(f.Data.Tree, f.Evaluator, null, true);
        using var originalBox = f.Doc.Objects.FindId(f.Box)!.Geometry.Duplicate();
        using var originalSource = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        using var originalResult = f.Evaluator.Find(f.Bend)!.Results.Single().DuplicateBrep();
        var transform = Transform.Translation(23, -18, 9) * Transform.Rotation(0.7, Vector3d.ZAxis, Point3d.Origin);
        var state = TreeStateCodec.Encode(f.Data.Capture());
        Check(WorkingResultTransform.Apply(f.Doc, f.Data, new[] { f.Bend }, transform,
            f.Working.Sources.IsManagedHidden, f.Working.Sources.IsLogicallyLocked, out var error),
            "Whole Bend rotation and translation carry managed hidden inputs and the Control Box: " + error);
        using var expectedBox = originalBox.Duplicate(); expectedBox.Transform(transform);
        using var expectedSource = originalSource.Duplicate(); expectedSource.Transform(transform);
        Check(GeometryBase.GeometryEquals(expectedBox, f.Doc.Objects.FindId(f.Box)!.Geometry) &&
            GeometryBase.GeometryEquals(expectedSource, f.Doc.Objects.FindId(f.Source)!.Geometry) &&
            ControlBoxGeometry.TryGetBox(f.Doc.Objects.FindId(f.Box)!.Geometry, 0.001, out _, out _) &&
            state == TreeStateCodec.Encode(f.Data.Capture()),
            "Whole Bend keeps signed box axes readable and settings unchanged while transforming each owned source once");
        f.Rebuild();
        originalResult.Transform(transform);
        Check(f.Evaluator.Find(f.Bend) is { IsCurrent: true } current &&
            BoundingError(current.Results.Single().GetBoundingBox(true), originalResult.GetBoundingBox(true)) < 0.005,
            "Recomputed whole Bend matches the rigidly transformed result");
        Check(f.Doc.Undo() && GeometryBase.GeometryEquals(originalBox, f.Doc.Objects.FindId(f.Box)!.Geometry) &&
            GeometryBase.GeometryEquals(originalSource, f.Doc.Objects.FindId(f.Source)!.Geometry) &&
            f.Doc.Objects.FindId(f.Box)!.IsHidden && f.Doc.Objects.FindId(f.Source)!.IsHidden,
            "One whole Bend Undo restores hidden control and input geometry together");
    }

    private static void LiveControlAndCache()
    {
        using var f = new Fixture();
        using var live = new LiveTreePreview();
        var initial = f.Evaluator.Find(f.Bend)!.Results.Single().GetBoundingBox(true);
        var boxCrc = f.Doc.Objects.FindId(f.Box)!.Geometry.DataCRC(0);
        var sourceCrc = f.Doc.Objects.FindId(f.Source)!.Geometry.DataCRC(0);
        var controlMove = Transform.Translation(3, 0, 0);
        Check(live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform> { [f.Box] = controlMove },
            0.001, f.Evaluator, f.Control) && live.Find(f.Bend) is { IsCurrent: true },
            "Dragging only Control Box recomputes Bend from its temporary native transform");
        var moved = live.Find(f.Bend)!.Results.Single().GetBoundingBox(true);
        Check(BoundingError(initial, moved) > 0.1 && live.LastReusedModifierCount == 0 &&
            boxCrc == f.Doc.Objects.FindId(f.Box)!.Geometry.DataCRC(0) && sourceCrc == f.Doc.Objects.FindId(f.Source)!.Geometry.DataCRC(0),
            "Control-only live drag changes the result without moving inputs or reusing the whole-result translation shortcut");
        Check(!live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform> { [f.Box] = controlMove },
            0.001, f.Evaluator, f.Control), "An unchanged Control Box drag skips redundant Bend calculations");
        var shear = Transform.Identity; shear.M01 = 0.2;
        Check(live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform> { [f.Box] = shear },
            0.001, f.Evaluator, f.Control) && live.Find(f.Bend) is { HasResult: true, IsCurrent: false },
            "A damaged live Control Box keeps the last valid Bend result with an error");
        Check(live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform> { [f.Box] = controlMove },
            0.001, f.Evaluator, f.Control) && live.Find(f.Bend) is { IsCurrent: true },
            "Returning to a valid Control Box transform recovers live Bend");
        live.Clear();
        var translation = Transform.Translation(10, 20, 30);
        Check(live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform>
            { [f.Box] = translation, [f.Source] = translation }, 0.001, f.Evaluator, f.Bend) &&
            live.LastReusedModifierCount == 1 && live.Find(f.Bend) is { IsCurrent: true },
            "Dragging the entire Bend reuses its result when the box and every input share the same translation");
        var translatedAt45 = live.Find(f.Bend)!.Results.Single().GetBoundingBox(true);
        Check(BendControlEdit.SetProperties(f.Doc, f.Data, f.Control, "Live box", new ControlBoxSettings(70, false), out _),
            "Control Box properties enter the committed tree state");
        Check(live.Update(f.Data.Tree, f.Read, new Dictionary<Guid, Transform>
            { [f.Box] = translation, [f.Source] = translation }, 0.001, f.Evaluator, f.Bend) &&
            live.Find(f.Bend) is { IsCurrent: true } changed &&
            Math.Abs(Volume(changed.Results.Single()) - 8000) < 0.1 &&
            BoundingError(translatedAt45, changed.Results.Single().GetBoundingBox(true)) > 0.1,
            "Changing Strength invalidates a live cache even when the cursor transform is unchanged");
    }

    private static double Volume(Brep b) { using var mass = VolumeMassProperties.Compute(b); return mass!.Volume; }
    private static double BoundingError(BoundingBox a, BoundingBox b) => a.Min.DistanceTo(b.Min) + a.Max.DistanceTo(b.Max);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Doc { get; } = RhinoDoc.CreateHeadless(null);
        public TreeDocumentData Data { get; }
        public TreeEvaluator Evaluator { get; } = new();
        public WorkingResultObjects Working { get; }
        public Guid Source { get; }
        public Guid Box { get; }
        public Guid Control { get; }
        public Guid Bend { get; }
        public Fixture()
        {
            Doc.ModelAbsoluteTolerance = 0.001;
            Doc.UndoRecordingEnabled = true;
            Data = new TreeDocumentData(Doc);
            Working = new WorkingResultObjects(Doc);
            using var source = new BoundingBox(-5, 0, -4, 5, 100, 4).ToBrep();
            Source = Doc.Objects.AddBrep(source);
            Data.Tree.RegisterSource(Source);
            Bend = Data.Tree.AddModifier(TreeNodeKind.Bend).Id;
            Data.Tree.Move(Data.Tree.FindSource(Source)!.Id, Bend, 0, out _);
            using var box = ControlBoxGeometry.Create(new Plane(new Point3d(0, 50, 0), Vector3d.XAxis, Vector3d.YAxis), new Vector3d(10, 100, 8));
            Box = Doc.Objects.AddBrep(box);
            Data.Tree.AddControlBox(Bend, Box, out _);
            Control = Data.Tree.FindSource(Box)!.Id;
            Data.Tree.SetControlBoxSettings(Control, new ControlBoxSettings(45, true), out _);
            Rebuild();
            Doc.ClearUndoRecords(true);
            Doc.Modified = false;
        }
        public GeometryBase? Read(Guid id) => Doc.Objects.FindId(id)?.Geometry;
        public void Rebuild() => Evaluator.Rebuild(Data.Tree, Read, Doc.ModelAbsoluteTolerance);
        public void Dispose()
        {
            // The windowless host leaves UndoActive set after direct Undo. Native
            // objects are released with this test document; do not write a display
            // cleanup transaction while the host still reports an active Undo.
            if (!Doc.UndoActive && !Doc.RedoActive) Working.Dispose();
            Evaluator.Dispose(); Data.Dispose(); Doc.Dispose();
        }
    }
}
