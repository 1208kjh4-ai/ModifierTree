using System.Drawing;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class WorkingResultTransformChecks
{
    public static void Run()
    {
        WholeHiddenRotation();
        TranslationAndNormalization();
        MultipleRootsUndo();
        ExistingUndoRecord();
        MixedNativeSelection();
        LegacyPlaneTransform();
        ControlGeometryIsAuthoritative();
        NonuniformScale();
        Identity();
        foreach (var reason in new[] { "hidden", "locked", "logical", "layer", "parent-layer", "missing", "readonly", "singular", "non-affine", "frame", "source", "empty" })
            Rejection(reason);
        NotificationRollback();
    }

    private static void WholeHiddenRotation()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, new Point3d(2, 3, 4));
        var settings = f.Data.Tree.Find(f.Array)!.Array!;
        var mirrorSettings = f.Data.Tree.Find(f.Mirror)!.Mirror!;
        Check(f.Apply([f.Parent], rotation, out var error), "A working result rotates every managed hidden input and BasePlane: " + error);
        var updated = f.Data.Tree.Find(f.Array)!.Array!;
        Check(f.Transformed(rotation) && f.NativePresentationUnchanged(hidden: true) && f.Doc.NextUndoRecordSerialNumber == serial + 1 &&
              Near(updated.AxisX, new ModifierVector(0, 1, 0)) && Near(updated.AxisY, new ModifierVector(-1, 0, 0)) &&
              Near(updated.AxisZ, new ModifierVector(0, 0, 1)) && updated.Spacing == settings.Spacing &&
              f.Data.Tree.Find(f.Mirror)!.Mirror == mirrorSettings,
            "Working result rotation preserves source GUIDs, names, groups, visibility and selection while carrying nested Array axes exactly once");
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged() && f.NativePresentationUnchanged(hidden: true),
            "One working result Undo restores all hidden input and control geometry together with Array axes");
    }

    private static void TranslationAndNormalization()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = State(f.Data);
        var translation = Transform.Translation(12, -7, 5);
        Check(f.Apply([f.Parent, f.Array, f.Parent, f.Mirror], translation, out var error) && State(f.Data) == before &&
              f.Transformed(translation) && f.NativePresentationUnchanged(hidden: true),
            "Ancestor/descendant duplicates translate owned geometry only once and leave saved directions and control-backed Mirror settings unchanged: " + error);
        Check(f.Doc.Undo() && f.SourcesUnchanged() && State(f.Data) == before,
            "A geometry-only working result translation owns one complete Undo action");
    }

    private static void MultipleRootsUndo()
    {
        using var f = new Fixture();
        var other = f.Data.Tree.AddModifier(TreeNodeKind.Array);
        var otherId = AddBox(f.Doc, 30, "Other root input");
        f.Data.Tree.RegisterSource(otherId);
        f.Data.Tree.Move(f.Data.Tree.FindSource(otherId)!.Id, other.Id, 0, out _);
        using var original = f.Doc.Objects.FindId(otherId)!.Geometry.Duplicate();
        f.HideAll();
        f.Doc.Objects.Hide(otherId, true);
        f.Managed.Add(otherId);
        var before = State(f.Data);
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        Check(f.Apply([f.Parent, other.Id], rotation, out _) && f.Transformed(rotation) &&
              Near(f.Data.Tree.Find(f.Array)!.Array!.AxisX, new ModifierVector(0, 1, 0)) &&
              Near(f.Data.Tree.Find(other.Id)!.Array!.AxisX, new ModifierVector(0, 1, 0)) &&
              !GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(otherId)!.Geometry),
            "Multiple independent result roots transform their own inputs and frames in one transaction");
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged() &&
              GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(otherId)!.Geometry) && f.Doc.Objects.FindId(otherId)!.IsHidden,
            "One Undo restores every independently selected result root without exposing hidden inputs");
    }

    private static void ExistingUndoRecord()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = State(f.Data);
        var record = f.Doc.BeginUndoRecord("Native result rotation command");
        try
        {
            Check(record != 0 && f.Apply([f.Parent], Transform.Rotation(0.4, Vector3d.YAxis, Point3d.Origin), out _) &&
                  f.Doc.CurrentUndoRecordSerialNumber == record && f.Doc.UndoRecordingIsActive,
                "Working result source replacements and private frame changes join the caller's active native Undo record");
        }
        finally { if (record != 0) f.Doc.EndUndoRecord(record); }
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged(),
            "Undo of the calling native command restores working result sources and frames together");
    }

    private static void MixedNativeSelection()
    {
        using var f = new Fixture();
        f.HideAll();
        var exposedInput = f.Inputs[0];
        f.Doc.Objects.Show(exposedInput, true);
        f.Managed.Remove(exposedInput);
        var before = State(f.Data);
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        var record = f.Doc.BeginUndoRecord("Native mixed result and input rotation");
        try
        {
            Check(record != 0 && f.Doc.Objects.Transform(exposedInput, rotation, true) == exposedInput &&
                  WorkingResultTransform.Apply(f.Doc, f.Data, [f.Parent], rotation, f.Managed.Contains, f.LogicalLocks.Contains,
                      out _, new HashSet<Guid> { exposedInput }) && f.Transformed(rotation),
                "A mixed selection of a result and its exposed input transforms each owned source exactly once");
            Check(Near(f.Data.Tree.Find(f.Array)!.Array!.AxisX, new ModifierVector(0, 1, 0)) &&
                  f.Doc.Objects.FindId(exposedInput) is { IsHidden: false } && f.Doc.Objects.FindId(f.Control) is { IsHidden: true },
                "Mixed native selection still rotates the full Array frame and preserves each input's presentation state");
        }
        finally { if (record != 0) f.Doc.EndUndoRecord(record); }
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged(),
            "One mixed native selection Undo restores both Rhino-transformed and managed-hidden sources together");
    }

    private static void LegacyPlaneTransform()
    {
        using var f = new Fixture(withControl: false);
        f.HideAll();
        var before = State(f.Data);
        var original = f.Data.Tree.Find(f.Mirror)!.Mirror!;
        var plane = new Plane(new Point3d(original.Origin.X, original.Origin.Y, original.Origin.Z),
            new Vector3d(original.Normal.X, original.Normal.Y, original.Normal.Z));
        var transform = Transform.Translation(10, 20, 30) * Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        plane.Transform(transform);
        Check(f.Apply([f.Parent], transform, out _) && f.Transformed(transform),
            "A legacy Mirror without BasePlane supports whole result translation and rotation");
        var updated = f.Data.Tree.Find(f.Mirror)!.Mirror!;
        Check(Near(updated.Origin, new ModifierVector(plane.OriginX, plane.OriginY, plane.OriginZ)) &&
              Near(updated.Normal, new ModifierVector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z)) &&
              updated.KeepOriginal == original.KeepOriginal && updated.Union == original.Union,
            "A legacy Mirror's numerical plane follows its hidden sources while retaining Mirror choices");
        Check(f.Doc.Undo() && State(f.Data) == before && f.SourcesUnchanged(),
            "One working result Undo also restores a legacy Mirror's numerical plane");
    }

    private static void ControlGeometryIsAuthoritative()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = f.Data.Tree.Find(f.Mirror)!.Mirror!;
        var rotation = Transform.Rotation(Math.PI / 2, Vector3d.YAxis, Point3d.Origin);
        Check(f.Apply([f.Mirror], rotation, out _) && f.Data.Tree.Find(f.Mirror)!.Mirror == before &&
              MirrorPlaneGeometry.TryGetPlane(f.Doc.Objects.FindId(f.Control)!.Geometry, 0.001, out var plane) &&
              Math.Abs(plane.Normal * Vector3d.ZAxis) > 0.999999,
            "A control-backed Mirror transforms its native plane without applying its numerical fallback a second time");
    }

    private static void NonuniformScale()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = f.Data.Tree.Find(f.Array)!.Array!;
        var transform = Transform.Scale(Plane.WorldXY, 2, 3, 4);
        Check(f.Apply([f.Parent], transform, out _) && f.Transformed(transform),
            "A nonsingular nonuniform result scale transforms managed hidden geometry");
        var after = f.Data.Tree.Find(f.Array)!.Array!;
        Check(after.AxisX == before.AxisX && after.AxisY == before.AxisY && after.AxisZ == before.AxisZ &&
              after.Spacing == new ModifierVector(before.Spacing.X * 2, before.Spacing.Y * 3, before.Spacing.Z * 4),
            "Working result scale carries Array placement distances as well as source shape");
    }

    private static void Identity()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(f.Apply([f.Parent], Transform.Identity, out _) && f.SourcesUnchanged() && State(f.Data) == before &&
              f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "An identity working result transform does not alter geometry, settings, modified state or Undo history");
    }

    private static void Rejection(string reason)
    {
        using var f = new Fixture();
        var target = f.Parent;
        var transform = Transform.Translation(10, 20, 30);
        var input = f.Inputs[^1]; // Reject a late source to check preflight protects earlier siblings and the control.
        if (reason == "hidden") f.Doc.Objects.Hide(input, true);
        if (reason == "locked") f.Doc.Objects.Lock(input, true);
        if (reason == "logical") { f.HideAll(); f.LogicalLocks.Add(input); }
        if (reason is "layer" or "parent-layer")
        {
            using var parent = new Layer { Name = "Locked input layer", IsLocked = true };
            var index = f.Doc.Layers.Add(parent);
            if (reason == "parent-layer")
            {
                using var child = new Layer { Name = "Child input layer", ParentLayerId = f.Doc.Layers[index].Id };
                index = f.Doc.Layers.Add(child);
            }
            using var attributes = f.Doc.Objects.FindId(input)!.Attributes.Duplicate();
            attributes.LayerIndex = index;
            f.Doc.Objects.ModifyAttributes(input, attributes, true);
        }
        if (reason == "missing") f.Doc.Objects.Delete(input, true);
        if (reason == "readonly") f.Data.ReadOnlyReason = "Unsupported document schema";
        if (reason == "singular") transform = Transform.Scale(Plane.WorldXY, 0, 1, 1);
        if (reason == "non-affine") { transform = Transform.Identity; transform.M30 = 0.1; }
        if (reason == "frame") transform = Transform.Scale(Point3d.Origin, 1e12);
        if (reason == "source") target = f.Data.Tree.FindSource(input)!.Id;
        if (reason == "empty") target = f.Data.Tree.AddModifier(TreeNodeKind.Array).Id;
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        var survivors = f.AllSources.Where(id => f.Doc.Objects.FindId(id) is not null).ToArray();
        f.Doc.Modified = false;
        Check(!f.Apply([target], transform, out var error) && error.Length > 0 && State(f.Data) == before &&
              f.SourcesUnchanged(survivors) && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            $"A {reason} working result request fails before changing any source, frame, modified flag or Undo record");
    }

    private static void NotificationRollback()
    {
        using var f = new Fixture();
        f.HideAll();
        var before = State(f.Data);
        var notifications = 0;
        f.Data.Changed += (_, _) => { if (++notifications == 1) throw new InvalidOperationException("Injected working result transform failure"); };
        Check(!f.Apply([f.Parent], Transform.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin), out var error) &&
              error.Contains("Injected") && State(f.Data) == before && f.SourcesUnchanged() &&
              f.NativePresentationUnchanged(hidden: true) && !f.Doc.Modified,
            "A transformed-frame notification failure restores all hidden source geometry, private settings and the original modified flag");
    }

    private static Guid AddBox(RhinoDoc doc, double x, string name)
    {
        using var geometry = new BoundingBox(x, 1, 0, x + 1, 2, 1).ToBrep();
        using var attributes = new ObjectAttributes { Name = name, ObjectColor = Color.CornflowerBlue, ColorSource = ObjectColorSource.ColorFromObject };
        return doc.Objects.AddBrep(geometry, attributes);
    }
    private static string State(TreeDocumentData data) => TreeStateCodec.Encode(data.Capture());
    private static bool Near(ModifierVector first, ModifierVector second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) + Math.Abs(first.Z - second.Z) < 1e-8;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Doc { get; } = RhinoDoc.CreateHeadless(null);
        public TreeDocumentData Data { get; }
        public Guid Parent { get; }
        public Guid Mirror { get; }
        public Guid Array { get; }
        public Guid Control { get; }
        public Guid[] Inputs { get; }
        public Guid[] AllSources { get; }
        public HashSet<Guid> Managed { get; } = [];
        public HashSet<Guid> LogicalLocks { get; } = [];
        private readonly Dictionary<Guid, GeometryBase> _originals = [];
        private readonly Dictionary<Guid, ObjectAttributes> _attributes = [];

        public Fixture(bool withControl = true)
        {
            Doc.UndoRecordingEnabled = true;
            Doc.ModelAbsoluteTolerance = 0.001;
            var tree = new ModifierTreeModel();
            Parent = tree.AddModifier(TreeNodeKind.BooleanUnion).Id;
            Mirror = tree.AddModifier(TreeNodeKind.Mirror).Id;
            Array = tree.AddModifier(TreeNodeKind.Array).Id;
            tree.Move(Mirror, Parent, 0, out _);
            tree.Move(Array, Mirror, 0, out _);
            tree.SetMirrorSettings(Mirror, MirrorSettings.Default with { Union = false, Origin = new ModifierVector(3, 4, 5) }, out _);
            tree.SetArraySettings(Array, ArraySettings.Default with { CountX = 3, CountY = 2, Spacing = new ModifierVector(4, -7, 2) }, out _);
            Inputs = [AddBox(Doc, 1, "Input A"), AddBox(Doc, 5, "Input B")];
            foreach (var id in Inputs)
            {
                tree.RegisterSource(id);
                tree.Move(tree.FindSource(id)!.Id, Array, tree.ChildrenOf(Array).Count, out _);
            }
            if (withControl)
            {
                using var surface = new PlaneSurface(new Plane(new Point3d(3, 4, 5), Vector3d.XAxis), new Interval(-10, 10), new Interval(-10, 10));
                using var geometry = surface.ToBrep();
                using var attributes = new ObjectAttributes { Name = "BasePlane", ObjectColor = Color.OrangeRed, ColorSource = ObjectColorSource.ColorFromObject };
                Control = Doc.Objects.AddBrep(geometry, attributes);
                tree.SetBasePlane(Mirror, Control, out _);
            }
            AllSources = tree.SourcesInSubtree(Parent).ToArray();
            Doc.Groups.Add("Original source group", AllSources);
            foreach (var id in AllSources)
            {
                var source = Doc.Objects.FindId(id)!;
                _originals.Add(id, source.Geometry.Duplicate());
                _attributes.Add(id, source.Attributes.Duplicate());
            }
            Data = new TreeDocumentData(Doc);
            Data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
            Doc.Modified = false;
        }
        public void HideAll()
        {
            foreach (var id in AllSources) { Doc.Objects.Hide(id, true); Managed.Add(id); }
            Doc.Modified = false;
        }
        public bool Apply(IReadOnlyCollection<Guid> nodes, Transform transform, out string error) =>
            WorkingResultTransform.Apply(Doc, Data, nodes, transform, Managed.Contains, LogicalLocks.Contains, out error);
        public bool SourcesUnchanged(IEnumerable<Guid>? ids = null) =>
            (ids ?? AllSources).All(id => GeometryBase.GeometryEquals(_originals[id], Doc.Objects.FindId(id)!.Geometry));
        public bool Transformed(Transform transform) => AllSources.All(id =>
        {
            using var expected = _originals[id].Duplicate();
            expected.Transform(transform);
            var actual = Doc.Objects.FindId(id)?.Geometry.GetBoundingBox(true) ?? BoundingBox.Empty;
            var bounds = expected.GetBoundingBox(true);
            return actual.IsValid && bounds.Min.DistanceTo(actual.Min) < 1e-8 && bounds.Max.DistanceTo(actual.Max) < 1e-8;
        });
        public bool NativePresentationUnchanged(bool hidden) => AllSources.All(id =>
        {
            var source = Doc.Objects.FindId(id);
            var original = _attributes[id];
            return source is not null && source.IsHidden == hidden && source.IsSelected(false) == 0 &&
                source.Attributes.Name == original.Name && source.Attributes.ObjectColor == original.ObjectColor &&
                source.Attributes.LayerIndex == original.LayerIndex && source.Attributes.ColorSource == original.ColorSource &&
                (source.Attributes.GetGroupList() ?? []).SequenceEqual(original.GetGroupList() ?? []);
        });
        public void Dispose()
        {
            Data.Dispose();
            foreach (var geometry in _originals.Values) geometry.Dispose();
            foreach (var attributes in _attributes.Values) attributes.Dispose();
            Doc.Dispose();
        }
    }
}
