using System.Drawing;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class ControlDocumentChecks
{
    public static void Run()
    {
        AddMirrorUndo();
        SetPlaneUndo();
        SetPlaneNativeOnlyUndo();
        SetPlaneRejections();
        SetPlaneRollback();
        foreach (var mode in new[] { ResultCommitMode.Bake, ResultCommitMode.Merge })
        {
            CommitPlaneInvariant(mode);
            CommitMovementIndependence(mode);
        }
        ClonePlaneIndependence();
        ClonePlaneMovementIndependence();
        RemoveMirrorUndo();
        ArrayAxisUndo();
        ArrayAxisRejections();
        WholeTransformUndo();
        TrackerIgnoresNonWholeTransforms();
        TrackerCancellationAndPartialTransform();
    }

    private static void AddMirrorUndo()
    {
        using var doc = NewDocument();
        using var data = new TreeDocumentData(doc);
        var before = State(data);
        var serial = doc.NextUndoRecordSerialNumber;
        Check(MirrorPlaneEdit.AddMirror(doc, data, out var mirrorId, out var error), "Add Mirror creates its editable BasePlane: " + error);
        var mirror = data.Tree.Find(mirrorId)!;
        var control = data.Tree.Find(mirror.Children.Single())!;
        var native = doc.Objects.FindId(control.ObjectId!.Value)!;
        Check(mirror.Mirror is { KeepOriginal: true, Union: true } && control.IsControl && !control.IsModifier &&
              native.Geometry is Brep brep && !brep.IsSolid && brep.Faces.Count == 1 &&
              brep.Faces[0].TryGetPlane(out var plane) && plane.Origin.DistanceTo(Point3d.Origin) < 1e-8 &&
              Math.Abs(plane.Normal * Vector3d.XAxis) > 0.999999 && native.Attributes.Name == "BasePlane" &&
              doc.NextUndoRecordSerialNumber == serial + 1,
            "A new Mirror owns one finite native YZ plane, defaults Union on, and opens only one Undo record");
        Check(doc.Undo() && State(data) == before && doc.Objects.FindId(native.Id) is null && ActiveIds(doc).Count == 0,
            "One Add Mirror Undo removes both the control object and its entire new tree entry");
    }

    private static void SetPlaneUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var oldPlane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        using var oldSource = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        var oldNodeId = f.Data.Tree.FindSource(f.Plane)!.Id;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        var plane = new Plane(new Point3d(4, 5, 6), new Point3d(5, 6, 6), new Point3d(4, 5, 8));
        Check(MirrorPlaneEdit.SetPlane(f.Doc, f.Data, f.Mirror, plane, out var error), "Three-point Mirror plane can be applied to an existing control: " + error);
        var changed = (Brep)f.Doc.Objects.FindId(f.Plane)!.Geometry;
        var settings = f.Data.Tree.Find(f.Mirror)!.Mirror!;
        Check(f.Data.Tree.FindSource(f.Plane)!.Id == oldNodeId && ActiveIds(f.Doc).Count == 2 &&
              !GeometryBase.GeometryEquals(changed, oldPlane) && changed.Faces[0].TryGetPlane(out var actual) &&
              actual.Origin.DistanceTo(plane.Origin) < 1e-8 && Math.Abs(actual.Normal * plane.Normal) > 0.999999 &&
              settings.Origin == new ModifierVector(4, 5, 6) && !settings.Union &&
              GeometryBase.GeometryEquals(oldSource, f.Doc.Objects.FindId(f.Source)!.Geometry) &&
              f.Doc.NextUndoRecordSerialNumber == serial + 1,
            "Set Plane preserves control GUID, tree identity, input geometry and Union choice while changing native plane and saved fallback together");
        Check(f.Doc.Undo() && State(f.Data) == before && GeometryBase.GeometryEquals(oldPlane, f.Doc.Objects.FindId(f.Plane)!.Geometry) &&
              GeometryBase.GeometryEquals(oldSource, f.Doc.Objects.FindId(f.Source)!.Geometry),
            "One Set Plane Undo restores both native control geometry and the complete saved tree settings");
    }

    private static void SetPlaneNativeOnlyUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var oldPlane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        Check(MirrorPlaneEdit.SetPlane(f.Doc, f.Data, f.Mirror, new Plane(Point3d.Origin, Vector3d.XAxis), out _) &&
              State(f.Data) == before && !GeometryBase.GeometryEquals(oldPlane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            "Setting the same infinite plane can resize only its finite control without manufacturing a private tree edit");
        Check(f.Doc.Undo() && State(f.Data) == before && GeometryBase.GeometryEquals(oldPlane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            "A native-only Set Plane change remains independently undoable");
    }

    private static void SetPlaneRejections()
    {
        foreach (var reason in new[] { "invalid", "locked", "hidden", "readonly", "source" })
        {
            using var f = new Fixture();
            if (reason == "locked") f.Doc.Objects.Lock(f.Plane, true);
            if (reason == "hidden") f.Doc.Objects.Hide(f.Plane, true);
            if (reason == "readonly") f.Data.ReadOnlyReason = "Unsupported saved schema";
            var before = State(f.Data);
            var ids = ActiveIds(f.Doc);
            using var geometry = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
            var serial = f.Doc.NextUndoRecordSerialNumber;
            f.Doc.Modified = false;
            var target = reason == "source" ? f.Data.Tree.FindSource(f.Source)!.Id : f.Mirror;
            var plane = reason == "invalid" ? Plane.Unset : Plane.WorldXY;
            Check(!MirrorPlaneEdit.SetPlane(f.Doc, f.Data, target, plane, out var error) && error.Length > 0 &&
                  State(f.Data) == before && ids.SetEquals(ActiveIds(f.Doc)) && !f.Doc.Modified &&
                  GeometryBase.GeometryEquals(geometry, f.Doc.Objects.FindId(f.Plane)!.Geometry) &&
                  f.Doc.NextUndoRecordSerialNumber == serial,
                $"A {reason} Set Plane request changes no native geometry, tree, modified flag or Undo history");
        }
    }

    private static void SetPlaneRollback()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var geometry = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        var notifications = 0;
        f.Data.Changed += (_, _) => { if (++notifications == 1) throw new InvalidOperationException("Injected Set Plane failure"); };
        Check(!MirrorPlaneEdit.SetPlane(f.Doc, f.Data, f.Mirror, Plane.WorldXY, out var error) && error.Contains("Injected") &&
              State(f.Data) == before && ActiveIds(f.Doc).Count == 2 &&
              GeometryBase.GeometryEquals(geometry, f.Doc.Objects.FindId(f.Plane)!.Geometry) && !f.Doc.Modified,
            "A Set Plane notification failure rolls back the native replacement and private state together");
    }

    private static void CommitPlaneInvariant(ResultCommitMode mode)
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var plane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        using var input = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        var planeSerial = f.Doc.Objects.FindId(f.Plane)!.RuntimeSerialNumber;
        Check(ResultCommit.Apply(f.Doc, f.Data, f.Mirror, mode, out var resultNodeId, out var error),
            $"{mode} creates a Mirror result containing only reflected input geometry: " + error);
        var resultId = f.Data.Tree.Find(resultNodeId)!.ObjectId!.Value;
        var result = f.Doc.Objects.FindId(resultId)!;
        Check(result.Geometry is Brep brep && brep.IsSolid && Math.Abs(Volume(brep) - 2) < 0.001 &&
              result.Attributes.ObjectColor.ToArgb() == Color.CornflowerBlue.ToArgb() && result.Attributes.Name == "Main" &&
              result.Attributes.GroupCount == 0 && GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry) &&
              GeometryBase.GeometryEquals(input, f.Doc.Objects.FindId(f.Source)!.Geometry),
            $"{mode} excludes BasePlane faces and appearance and preserves both control and original input geometry exactly");
        var removed = mode == ResultCommitMode.Merge;
        Check(f.Doc.Objects.FindId(f.Plane)!.IsHidden == removed && f.Doc.Objects.FindId(f.Source)!.IsHidden == removed &&
              (f.Data.Tree.FindSource(f.Plane) is null) == removed &&
              (removed || f.Doc.Objects.FindId(f.Plane)!.RuntimeSerialNumber == planeSerial),
            $"{mode} {(removed ? "hides and unregisters" : "retains unchanged")} BasePlane at its original pose");
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(resultId) is null &&
              f.Doc.Objects.FindId(f.Plane) is { IsHidden: false } && f.Doc.Objects.FindId(f.Source) is { IsHidden: false } &&
              GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            $"One {mode} Undo restores the complete Mirror tree and control visibility without changing its plane");
    }

    private static void CommitMovementIndependence(ResultCommitMode mode)
    {
        using var f = new Fixture();
        using var plane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        using var input = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        if (!ResultCommit.Apply(f.Doc, f.Data, f.Mirror, mode, out var resultNodeId, out var error))
            throw new Exception("Could not prepare independent result movement fixture: " + error);
        var resultId = f.Data.Tree.Find(resultNodeId)!.ObjectId!.Value;
        var record = f.Doc.BeginUndoRecord("Move committed result independently");
        try
        {
            Check(record != 0 && f.Doc.Objects.Transform(resultId, Transform.Translation(20, 30, 40), true) == resultId &&
                  GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry) &&
                  GeometryBase.GeometryEquals(input, f.Doc.Objects.FindId(f.Source)!.Geometry),
                $"Moving a {mode} result never carries along the retained control or original input");
        }
        finally { if (record != 0) f.Doc.EndUndoRecord(record); }
    }

    private static void ClonePlaneIndependence()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var plane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        Check(SubtreeDuplicate.Apply(f.Doc, f.Data, f.Mirror, out var copyId, out var error), "Duplicate includes a new independent BasePlane: " + error);
        var copyPlane = f.Data.Tree.Find(copyId)!.Children.Select(f.Data.Tree.Find).Single(node => node!.IsControl)!;
        var copiedIds = f.Data.Tree.SourcesInSubtree(copyId);
        Check(copyPlane.ObjectId != f.Plane && copiedIds.Count == 2 && !copiedIds.Contains(f.Source) && !copiedIds.Contains(f.Plane) &&
              GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(copyPlane.ObjectId!.Value)!.Geometry),
            "A duplicate Mirror has an independent native control and input while retaining the original plane pose");
        Check(f.Doc.Undo() && State(f.Data) == before && copiedIds.All(id => f.Doc.Objects.FindId(id) is null) &&
              GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            "One duplicate Undo removes the copied plane and inputs without touching the original Mirror");
    }

    private static void ClonePlaneMovementIndependence()
    {
        using var f = new Fixture();
        using var plane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        if (!SubtreeDuplicate.Apply(f.Doc, f.Data, f.Mirror, out var copyId, out var error))
            throw new Exception("Could not prepare independent control movement fixture: " + error);
        var copyPlane = f.Data.Tree.Find(copyId)!.Children.Select(f.Data.Tree.Find).Single(node => node!.IsControl)!;
        var record = f.Doc.BeginUndoRecord("Move copied control independently");
        try
        {
            Check(record != 0 && f.Doc.Objects.Transform(copyPlane.ObjectId!.Value, Transform.Translation(5, 0, 0), true) == copyPlane.ObjectId.Value &&
                  GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry) &&
                  !GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(copyPlane.ObjectId.Value)!.Geometry),
                "Moving a duplicated control leaves its original Mirror plane independent");
        }
        finally { if (record != 0) f.Doc.EndUndoRecord(record); }
    }

    private static void ArrayAxisUndo()
    {
        using var f = new ArrayFixture();
        var before = State(f.Data);
        var initial = f.Data.Tree.Find(f.Array)!.Array!;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(ArrayAxisEdit.Apply(f.Data, f.Array, 0, new Vector3d(0, 30, 0), out var error), "Set Axis accepts the direction between two picked points: " + error);
        var updated = f.Data.Tree.Find(f.Array)!.Array!;
        Check(updated.AxisX == new ModifierVector(0, 1, 0) && updated.AxisY == initial.AxisY && updated.AxisZ == initial.AxisZ &&
              updated.Spacing == initial.Spacing && updated.CountX == initial.CountX && f.SourcesUnchanged() &&
              f.Doc.NextUndoRecordSerialNumber == serial + 1,
            "Set Axis normalizes pick distance into direction without changing spacing, counts, other axes or original geometry");
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged(), "One Set Axis Undo restores the previous directions and complete tree state");
    }

    private static void RemoveMirrorUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        using var plane = f.Doc.Objects.FindId(f.Plane)!.Geometry.Duplicate();
        var planeNodeId = f.Data.Tree.FindSource(f.Plane)!.Id;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(!TreeItemRemoval.Apply(f.Doc, f.Data, planeNodeId, out _) && State(f.Data) == before &&
              f.Doc.NextUndoRecordSerialNumber == serial,
            "Removing a BasePlane directly cannot orphan a Mirror control or add Undo history");
        Check(TreeItemRemoval.Apply(f.Doc, f.Data, f.Mirror, out var error) && f.Data.Tree.Find(f.Mirror) is null &&
              f.Data.Tree.FindSource(f.Plane) is null && f.Doc.Objects.FindId(f.Plane) is { IsHidden: true } &&
              f.Data.Tree.FindSource(f.Source)!.ParentId is null && f.Doc.Objects.FindId(f.Source) is { IsHidden: false } &&
              GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            "Removing Mirror hides its owned control in place and promotes its ordinary input: " + error);
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(f.Plane) is { IsHidden: false } &&
              GeometryBase.GeometryEquals(plane, f.Doc.Objects.FindId(f.Plane)!.Geometry),
            "One Remove Mirror Undo restores its control row, input hierarchy and native plane visibility");
    }

    private static void ArrayAxisRejections()
    {
        using var f = new ArrayFixture();
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(ArrayAxisEdit.Apply(f.Data, f.Array, 0, Vector3d.XAxis * 500, out _) &&
              f.Doc.NextUndoRecordSerialNumber == serial && State(f.Data) == before && !f.Doc.Modified,
            "Picking the same Array direction at a different distance adds no artificial settings edit or Undo record");
        Check(!ArrayAxisEdit.Apply(f.Data, f.Array, 0, Vector3d.Zero, out _) &&
              !ArrayAxisEdit.Apply(f.Data, f.Array, 0, Vector3d.Unset, out _) &&
              !ArrayAxisEdit.Apply(f.Data, f.Array, 3, Vector3d.XAxis, out _) &&
              !ArrayAxisEdit.Apply(f.Data, f.Data.Tree.FindSource(f.Sources[0])!.Id, 0, Vector3d.YAxis, out _) &&
              State(f.Data) == before && f.Doc.NextUndoRecordSerialNumber == serial,
            "Degenerate picks, invalid axis indices and non-Array targets cannot change Array settings or history");
        f.Data.ReadOnlyReason = "Unsupported saved schema";
        Check(!ArrayAxisEdit.Apply(f.Data, f.Array, 1, Vector3d.ZAxis, out _) &&
              State(f.Data) == before && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "Read-only document data rejects Set Axis before changing settings or Undo history");
    }

    private static void WholeTransformUndo()
    {
        using var f = new ArrayFixture();
        using var tracker = new PatternTransformTracker(f.Doc, f.Data, () => null);
        var before = State(f.Data);
        var original = f.Data.Tree.Find(f.Array)!.Array!;
        var serial = f.Doc.BeginUndoRecord("Whole ancestor rotation including nested Array");
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        try
        {
            Check(serial != 0 && tracker.Begin(501, f.Sources, rotation, false, f.Parent, out _) &&
                  SubtreeTransform.Apply(f.Doc, f.Data.Tree, f.Parent, rotation, out _) && tracker.Complete(501, out var error),
                "An ancestor native rotation can commit nested Array directions inside the same active Undo record");
            var updated = f.Data.Tree.Find(f.Array)!.Array!;
            Check(Near(updated.AxisX, new ModifierVector(0, 1, 0)) && Near(updated.AxisY, new ModifierVector(-1, 0, 0)) &&
                  Near(updated.AxisZ, new ModifierVector(0, 0, 1)) && updated.Spacing == original.Spacing &&
                  !f.SourcesUnchanged() && f.Doc.CurrentUndoRecordSerialNumber == serial && f.Doc.UndoRecordingIsActive,
                "Whole ancestor rotation carries descendant Array axes and all source geometry together without opening another record");
        }
        finally { if (serial != 0) f.Doc.EndUndoRecord(serial); }
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged(),
            "One native rotation Undo restores original source geometry and all nested Array directions together");
    }

    private static void TrackerIgnoresNonWholeTransforms()
    {
        foreach (var mode in new[] { "child", "copy", "translation" })
        {
            using var f = new ArrayFixture();
            using var tracker = new PatternTransformTracker(f.Doc, f.Data, () => null);
            var before = State(f.Data);
            var serial = f.Doc.BeginUndoRecord(mode + " transform");
            try
            {
                var transform = mode == "translation" ? Transform.Translation(9, 8, 7) : Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
                var selected = mode == "child" ? f.Data.Tree.FindSource(f.Sources[0])!.Id : f.Array;
                var ids = mode == "child" ? new[] { f.Sources[0] } : f.Sources;
                Check(tracker.Begin(502, ids, transform, mode == "copy", selected, out _), $"Tracker accepts a {mode} native transform");
                if (mode != "copy") foreach (var id in ids) f.Doc.Objects.Transform(id, transform, true);
                else foreach (var id in ids) f.Doc.Objects.Transform(id, transform, false);
                Check(tracker.Complete(502, out _) && State(f.Data) == before,
                    $"A {mode} transform leaves original Array directions and spacing unchanged");
            }
            finally { if (serial != 0) f.Doc.EndUndoRecord(serial); }
        }
    }

    private static void TrackerCancellationAndPartialTransform()
    {
        using var f = new ArrayFixture();
        using var tracker = new PatternTransformTracker(f.Doc, f.Data, () => null);
        var before = State(f.Data);
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        var serial = f.Doc.BeginUndoRecord("Cancelled and partial transform");
        try
        {
            Check(tracker.Begin(503, f.Sources, rotation, false, f.Parent, out _) && tracker.Complete(503, out _) && State(f.Data) == before,
                "A cancelled native drag with unchanged source objects cannot rotate saved Array axes");
            Check(tracker.Begin(504, f.Sources, rotation, false, f.Parent, out _) &&
                  f.Doc.Objects.Transform(f.Sources[0], rotation, true) == f.Sources[0] && tracker.Complete(504, out _) && State(f.Data) == before,
                "A partially committed native transform cannot apply an entire subtree's Array frame change");
            Check(tracker.Begin(505, f.Sources, rotation, false, f.Parent, out _), "A pending transform can be explicitly cleared on cancellation");
            tracker.Clear();
            foreach (var id in f.Sources) f.Doc.Objects.Transform(id, rotation, true);
            Check(tracker.Complete(505, out _) && State(f.Data) == before, "A cleared transform event cannot replay stale Array axis changes later");
        }
        finally { if (serial != 0) f.Doc.EndUndoRecord(serial); }
    }

    private static RhinoDoc NewDocument()
    {
        var document = RhinoDoc.CreateHeadless(null);
        document.UndoRecordingEnabled = true;
        document.ModelAbsoluteTolerance = 0.001;
        return document;
    }
    private static string State(TreeDocumentData data) => TreeStateCodec.Encode(data.Capture());
    private static double Volume(Brep geometry) { using var mass = VolumeMassProperties.Compute(geometry); return mass!.Volume; }
    private static bool Near(ModifierVector first, ModifierVector second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) + Math.Abs(first.Z - second.Z) < 1e-8;
    private static HashSet<Guid> ActiveIds(RhinoDoc document) => document.Objects.GetObjectList(new ObjectEnumeratorSettings
        { NormalObjects = true, HiddenObjects = true, LockedObjects = true, DeletedObjects = false }).Select(obj => obj.Id).ToHashSet();
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Doc { get; } = NewDocument();
        public TreeDocumentData Data { get; }
        public Guid Mirror { get; }
        public Guid Source { get; }
        public Guid Plane { get; }
        public Fixture()
        {
            var tree = new ModifierTreeModel();
            using var geometry = new BoundingBox(1, 0, 0, 2, 1, 1).ToBrep();
            using var sourceAttributes = new ObjectAttributes { Name = "Input", ObjectColor = Color.CornflowerBlue, ColorSource = ObjectColorSource.ColorFromObject };
            Source = Doc.Objects.AddBrep(geometry, sourceAttributes);
            tree.RegisterSource(Source);
            Mirror = tree.AddModifier(TreeNodeKind.Mirror).Id;
            tree.RenameModifier(Mirror, "Main", out _);
            tree.SetMirrorSettings(Mirror, MirrorSettings.Default with { Union = false }, out _);
            tree.Move(tree.FindSource(Source)!.Id, Mirror, 0, out _);
            using var surface = new PlaneSurface(new Plane(Point3d.Origin, Vector3d.XAxis), new Interval(-10, 10), new Interval(-10, 10));
            using var plane = surface.ToBrep();
            using var planeAttributes = new ObjectAttributes { Name = "BasePlane", ObjectColor = Color.OrangeRed, ColorSource = ObjectColorSource.ColorFromObject };
            Plane = Doc.Objects.AddBrep(plane, planeAttributes);
            tree.SetBasePlane(Mirror, Plane, out _);
            Doc.Groups.Add("Control and original input group", new[] { Plane, Source });
            Data = new TreeDocumentData(Doc);
            Data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
            Doc.Modified = false;
        }
        public void Dispose() { Data.Dispose(); Doc.Dispose(); }
    }

    private sealed class ArrayFixture : IDisposable
    {
        public RhinoDoc Doc { get; } = NewDocument();
        public TreeDocumentData Data { get; }
        public Guid Parent { get; }
        public Guid Array { get; }
        public Guid[] Sources { get; }
        private readonly Dictionary<Guid, GeometryBase> _originals = [];
        public ArrayFixture()
        {
            var tree = new ModifierTreeModel();
            Parent = tree.AddModifier(TreeNodeKind.BooleanUnion).Id;
            Array = tree.AddModifier(TreeNodeKind.Array).Id;
            tree.Move(Array, Parent, 0, out _);
            tree.SetArraySettings(Array, ArraySettings.Default with { CountX = 3, CountY = 2, Spacing = new ModifierVector(4, -7, 2) }, out _);
            foreach (var x in new[] { 1d, 3d })
            {
                using var geometry = new BoundingBox(x, 1, 0, x + 1, 2, 1).ToBrep();
                var id = Doc.Objects.AddBrep(geometry);
                tree.RegisterSource(id);
                tree.Move(tree.FindSource(id)!.Id, Array, tree.ChildrenOf(Array).Count, out _);
                _originals.Add(id, Doc.Objects.FindId(id)!.Geometry.Duplicate());
            }
            Sources = _originals.Keys.ToArray();
            Data = new TreeDocumentData(Doc);
            Data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
            Doc.Modified = false;
        }
        public bool SourcesUnchanged() => _originals.All(pair => GeometryBase.GeometryEquals(pair.Value, Doc.Objects.FindId(pair.Key)!.Geometry));
        public void Dispose() { Data.Dispose(); foreach (var geometry in _originals.Values) geometry.Dispose(); Doc.Dispose(); }
    }
}
