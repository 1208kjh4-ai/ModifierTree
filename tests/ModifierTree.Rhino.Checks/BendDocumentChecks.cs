using System.Drawing;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class BendDocumentChecks
{
    public static void Run()
    {
        AddUndo();
        AddEmpty();
        PropertyUndo();
        PropertyRejections();
        LockedAncestorRejections();
        CurvatureCollapseRejection();
        PropertyRollback();
        FitUndo();
        foreach (var mode in new[] { ResultCommitMode.Bake, ResultCommitMode.Merge })
        {
            CommitUndo(mode);
            CommitMovement(mode);
        }
        DuplicateUndo();
        DuplicateIndependence();
        RemoveUndo(wholeBend: false);
        RemoveUndo(wholeBend: true);
    }

    private static void AddUndo()
    {
        using var f = new Fixture(controlCount: 0);
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        using var source = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        Check(BendControlEdit.Add(f.Doc, f.Data, f.Bend, out var controlId, out var error),
            "Adding a Bend Control Box creates a native object and tree control: " + error);
        var node = f.Data.Tree.Find(controlId)!;
        var native = f.Doc.Objects.FindId(node.ObjectId!.Value)!;
        Check(node.ParentId == f.Bend && node.ControlBox == ControlBoxSettings.Default && !node.IsModifier && node.IsControl &&
            native.Attributes.Name == "Control Box" && ControlBoxGeometry.TryGetBox(native.Geometry, f.Doc.ModelAbsoluteTolerance, out var box, out _) &&
            Near(box.X.Length, 10) && Near(box.Y.Length, 100) && Near(box.Z.Length, 8) &&
            box.Center.DistanceTo(new Point3d(0, 50, 0)) < 1e-8 && f.Doc.NextUndoRecordSerialNumber == serial + 1 &&
            GeometryBase.GeometryEquals(source, f.Doc.Objects.FindId(f.Source)!.Geometry),
            "Add auto-fits the input, preserves source geometry, defaults to 45-degree Limited, and creates one Undo record");
        var nativeId = native.Id;
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(nativeId) is null &&
            ActiveIds(f.Doc).SetEquals([f.Source]),
            "One Add Control Box Undo removes both its native geometry and saved tree entry");
    }

    private static void AddEmpty()
    {
        using var doc = NewDocument();
        using var data = new TreeDocumentData(doc);
        var tree = new ModifierTreeModel();
        var bend = tree.AddModifier(TreeNodeKind.Bend);
        data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
        Check(BendControlEdit.Add(doc, data, bend.Id, out var id, out _) &&
            ControlBoxGeometry.TryGetBox(doc.Objects.FindId(data.Tree.Find(id)!.ObjectId!.Value)!.Geometry,
                doc.ModelAbsoluteTolerance, out var box, out _) &&
            Near(box.X.Length, 10) && Near(box.Y.Length, 10) && Near(box.Z.Length, 10) && box.Center.DistanceTo(Point3d.Origin) < 1e-8,
            "A Bend with no inputs can create a default control cube centered at the world origin");
        var serial = doc.NextUndoRecordSerialNumber;
        var before = State(data);
        doc.Modified = false;
        Check(!BendControlEdit.Fit(doc, data, id, out var error) && error.Length > 0 &&
            State(data) == before && doc.NextUndoRecordSerialNumber == serial && !doc.Modified,
            "Fit without geometry inputs reports the problem before writing or opening Undo history");
    }

    private static void PropertyUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var control = f.Controls[0];
        var nodeId = f.Data.Tree.FindSource(control)!.Id;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        using var geometry = f.Doc.Objects.FindId(control)!.Geometry.Duplicate();
        Check(BendControlEdit.SetProperties(f.Doc, f.Data, nodeId, "  Lower bend  ", new ControlBoxSettings(-70, false), out var error) &&
            f.Data.Tree.Find(nodeId)!.ControlBox == new ControlBoxSettings(-70, false) &&
            f.Doc.Objects.FindId(control)!.Attributes.Name == "Lower bend" &&
            f.Doc.NextUndoRecordSerialNumber == serial + 1 && GeometryBase.GeometryEquals(geometry, f.Doc.Objects.FindId(control)!.Geometry),
            "Name, signed Strength and mode commit together without changing native box geometry: " + error);
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(control)!.Attributes.Name == "Box 1" &&
            GeometryBase.GeometryEquals(geometry, f.Doc.Objects.FindId(control)!.Geometry),
            "One Control Box property Undo restores native name and private settings together");
    }

    private static void PropertyRejections()
    {
        using var f = new Fixture();
        var node = f.Data.Tree.FindSource(f.Controls[0])!;
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        f.Doc.Modified = false;
        Check(BendControlEdit.SetProperties(f.Doc, f.Data, node.Id, "Box 1", node.ControlBox!, out _) &&
            !BendControlEdit.SetProperties(f.Doc, f.Data, node.Id, "Should not rename", new ControlBoxSettings(double.NaN), out _) &&
            !BendControlEdit.SetProperties(f.Doc, f.Data, node.Id, "Should not rename", new ControlBoxSettings(181), out _) &&
            !BendControlEdit.SetProperties(f.Doc, f.Data, node.Id, "Invalid\nname", new ControlBoxSettings(30), out _) &&
            !BendControlEdit.SetProperties(f.Doc, f.Data, f.Bend, "Wrong selection", ControlBoxSettings.Default, out _) &&
            f.Doc.Objects.FindId(f.Controls[0])!.Attributes.Name == "Box 1" &&
            State(f.Data) == before && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "No-op, invalid Strength/name and wrong selection produce no partial rename, tree edit or Undo record");
    }

    private static void LockedAncestorRejections()
    {
        using var f = new Fixture();
        using var parentLayer = new Layer { Name = "Locked controls parent" };
        var parentIndex = f.Doc.Layers.Add(parentLayer);
        if (parentIndex < 0) throw new Exception("Cannot add Bend parent layer fixture.");
        using var childLayer = new Layer { Name = "Control boxes", ParentLayerId = f.Doc.Layers[parentIndex].Id };
        var childIndex = f.Doc.Layers.Add(childLayer);
        if (childIndex < 0) throw new Exception("Cannot add Bend child layer fixture.");
        var id = f.Controls[0];
        using (var attributes = f.Doc.Objects.FindId(id)!.Attributes.Duplicate())
        {
            attributes.LayerIndex = childIndex;
            if (!f.Doc.Objects.ModifyAttributes(id, attributes, true)) throw new Exception("Cannot assign Bend child layer fixture.");
        }
        var parent = f.Doc.Layers[parentIndex];
        parent.IsLocked = true;
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        using var original = f.Doc.Objects.FindId(id)!.Geometry.Duplicate();
        f.Doc.Modified = false;
        var nodeId = f.Data.Tree.FindSource(id)!.Id;
        Check(!BendControlEdit.Fit(f.Doc, f.Data, nodeId, out var fitError) && fitError.Length > 0 &&
            !BendControlEdit.SetProperties(f.Doc, f.Data, nodeId, "Must not rename", new ControlBoxSettings(70, false), out var propertyError) && propertyError.Length > 0 &&
            State(f.Data) == before && GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(id)!.Geometry) &&
            f.Doc.Objects.FindId(id)!.Attributes.Name == "Box 1" && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "Fit and properties respect a locked ancestor layer without changing box geometry, name, settings or Undo history");
    }

    private static void CurvatureCollapseRejection()
    {
        using var f = new Fixture();
        var id = f.Controls[0];
        var nodeId = f.Data.Tree.FindSource(id)!.Id;
        if (!BendControlEdit.SetProperties(f.Doc, f.Data, nodeId, "Flat baseline", new ControlBoxSettings(0), out var error))
            throw new Exception("Cannot prepare flat Bend settings: " + error);
        var frame = new Plane(new Point3d(0, 50, 0), Vector3d.XAxis, Vector3d.YAxis);
        var shrink = Transform.Scale(frame, 1, 10.0 / 110, 1);
        if (f.Doc.Objects.Transform(id, shrink, true) != id ||
            !ControlBoxGeometry.TryGetBox(f.Doc.Objects.FindId(id)!.Geometry, f.Doc.ModelAbsoluteTolerance, out var box, out _) || !Near(box.Y.Length, 10))
            throw new Exception("Cannot prepare short Control Box fixture.");
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        using var original = f.Doc.Objects.FindId(id)!.Geometry.Duplicate();
        f.Doc.Modified = false;
        Check(!BendControlEdit.SetProperties(f.Doc, f.Data, nodeId, "Must not rename", new ControlBoxSettings(180), out error) && error.Length > 0 &&
            f.Data.Tree.Find(nodeId)!.ControlBox == new ControlBoxSettings(0) &&
            f.Doc.Objects.FindId(id)!.Attributes.Name == "Flat baseline" && State(f.Data) == before &&
            GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(id)!.Geometry) && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "An in-range Strength that collapses the input across the bend radius is rejected before name, settings or Undo changes");
    }

    private static void PropertyRollback()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var notifications = 0;
        f.Data.Changed += (_, _) => { if (++notifications == 1) throw new InvalidOperationException("Injected Bend property failure"); };
        Check(!BendControlEdit.SetProperties(f.Doc, f.Data, f.Data.Tree.FindSource(f.Controls[0])!.Id,
                "Temporary", new ControlBoxSettings(80, false), out var error) && error.Contains("Injected") &&
            State(f.Data) == before && f.Doc.Objects.FindId(f.Controls[0])!.Attributes.Name == "Box 1" && !f.Doc.Modified,
            "A property notification failure rolls back both native name and Control Box settings");
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(f.Controls[0])!.Attributes.Name == "Box 1",
            "Undo after a rolled-back Bend property action does not resurrect partial settings");
    }

    private static void FitUndo()
    {
        using var f = new Fixture();
        var id = f.Controls[0];
        var rotation = Transform.Rotation(0.3, Vector3d.ZAxis, new Point3d(0, 50, 0));
        if (f.Doc.Objects.Transform(id, rotation, true) != id) throw new Exception("Cannot rotate Fit fixture.");
        using var original = f.Doc.Objects.FindId(id)!.Geometry.Duplicate();
        using var source = f.Doc.Objects.FindId(f.Source)!.Geometry.Duplicate();
        if (!ControlBoxGeometry.TryGetBox(original, f.Doc.ModelAbsoluteTolerance, out var oldBox, out var failure)) throw new Exception(failure);
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        var nodeId = f.Data.Tree.FindSource(id)!.Id;
        Check(BendControlEdit.Fit(f.Doc, f.Data, nodeId, out var error) &&
            ControlBoxGeometry.TryGetBox(f.Doc.Objects.FindId(id)!.Geometry, f.Doc.ModelAbsoluteTolerance, out var fitted, out _) &&
            fitted.Plane.XAxis * oldBox.Plane.XAxis > 0.999999 && fitted.Plane.YAxis * oldBox.Plane.YAxis > 0.999999 &&
            ContainsBounds(fitted, source.GetBoundingBox(true), f.Doc.ModelAbsoluteTolerance) &&
            !GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(id)!.Geometry) &&
            GeometryBase.GeometryEquals(source, f.Doc.Objects.FindId(f.Source)!.Geometry) && State(f.Data) == before &&
            f.Doc.Objects.FindId(id)!.Attributes.Name == "Box 1" && f.Doc.NextUndoRecordSerialNumber == serial + 1,
            "Fit encloses inputs while preserving box orientation, settings, name and source geometry: " + error);
        Check(f.Doc.Undo() && State(f.Data) == before && GeometryBase.GeometryEquals(original, f.Doc.Objects.FindId(id)!.Geometry),
            "One Fit Undo restores the exact previous native Control Box geometry");
    }

    private static void CommitUndo(ResultCommitMode mode)
    {
        using var f = new Fixture(controlCount: 2);
        var before = State(f.Data);
        using var expected = ExpectedResult(f);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(ResultCommit.Apply(f.Doc, f.Data, f.Bend, mode, out var resultNodeId, out var error),
            $"{mode} accepts a Bend containing two Control Boxes: " + error);
        var result = f.Doc.Objects.FindId(f.Data.Tree.Find(resultNodeId)!.ObjectId!.Value)!;
        var actual = (Brep)result.Geometry;
        var removed = mode == ResultCommitMode.Merge;
        Check(actual.IsSolid && actual.Faces.Count == expected.Faces.Count && Math.Abs(Volume(actual) - Volume(expected)) < 0.01 &&
            result.Attributes.Name == "Main bend" && result.Attributes.ObjectColor.ToArgb() == Color.CornflowerBlue.ToArgb() &&
            result.Attributes.GroupCount == 0 && f.OriginalsUnchanged() && f.Doc.NextUndoRecordSerialNumber == serial + 1 &&
            f.Controls.All(id => f.Doc.Objects.FindId(id)!.IsHidden == removed && (f.Data.Tree.FindSource(id) is null) == removed),
            $"{mode} includes only the bent result and source appearance, leaving all Control Boxes at their original poses");
        var resultId = result.Id;
        Check(f.Doc.Undo() && State(f.Data) == before && f.Doc.Objects.FindId(resultId) is null && f.OriginalsUnchanged() &&
            f.Controls.All(id => f.Doc.Objects.FindId(id) is { IsHidden: false }),
            $"One Bend {mode} Undo restores the tree and native control visibility together");
    }

    private static void CommitMovement(ResultCommitMode mode)
    {
        using var f = new Fixture();
        if (!ResultCommit.Apply(f.Doc, f.Data, f.Bend, mode, out var resultNodeId, out var error)) throw new Exception(error);
        var id = f.Data.Tree.Find(resultNodeId)!.ObjectId!.Value;
        using var originalResult = f.Doc.Objects.FindId(id)!.Geometry.Duplicate();
        var record = f.Doc.BeginUndoRecord("Move committed Bend independently");
        try
        {
            Check(record != 0 && f.Doc.Objects.Transform(id, Transform.Translation(40, 20, 10), true) == id &&
                f.OriginalsUnchanged() && !GeometryBase.GeometryEquals(originalResult, f.Doc.Objects.FindId(id)!.Geometry),
                $"Moving the {mode} result leaves retained Control Boxes and Bend inputs in place");
        }
        finally { if (record != 0) f.Doc.EndUndoRecord(record); }
    }

    private static void DuplicateUndo()
    {
        using var f = new Fixture(controlCount: 2);
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(SubtreeDuplicate.Apply(f.Doc, f.Data, f.Bend, out var copyId, out var error),
            "Duplicating Bend creates independent inputs and Control Boxes: " + error);
        var copies = f.Data.Tree.Find(copyId)!.Children.Select(id => f.Data.Tree.Find(id)!).Where(node => node.IsControl).ToArray();
        var copiedIds = f.Data.Tree.SourcesInSubtree(copyId);
        Check(copies.Length == 2 && copiedIds.Count == 3 && !copiedIds.Intersect(f.OriginalIds).Any() &&
            copies.Select((node, index) => node.ControlBox == f.Data.Tree.FindSource(f.Controls[index])!.ControlBox &&
                GeometryBase.GeometryEquals(f.Doc.Objects.FindId(node.ObjectId!.Value)!.Geometry, f.Doc.Objects.FindId(f.Controls[index])!.Geometry)).All(value => value) &&
            f.OriginalsUnchanged() && f.Doc.NextUndoRecordSerialNumber == serial + 1,
            "A Bend duplicate preserves ordered per-box Strength/mode/pose with new native object identities");
        Check(f.Doc.Undo() && State(f.Data) == before && copiedIds.All(id => f.Doc.Objects.FindId(id) is null) && f.OriginalsUnchanged(),
            "One duplicate Undo removes all copied Bend controls and input geometry together");
    }

    private static void DuplicateIndependence()
    {
        using var f = new Fixture();
        if (!SubtreeDuplicate.Apply(f.Doc, f.Data, f.Bend, out var copyId, out var error)) throw new Exception(error);
        var copy = f.Data.Tree.Find(copyId)!.Children.Select(f.Data.Tree.Find).Single(node => node!.IsControl)!;
        Check(BendControlEdit.SetProperties(f.Doc, f.Data, copy.Id, "Copied box", new ControlBoxSettings(-30, false), out _) &&
            f.Doc.Objects.Transform(copy.ObjectId!.Value, Transform.Translation(10, 0, 0), true) == copy.ObjectId.Value &&
            f.Data.Tree.FindSource(f.Controls[0])!.ControlBox == new ControlBoxSettings(30, true) &&
            f.Doc.Objects.FindId(f.Controls[0])!.Attributes.Name == "Box 1" && f.OriginalsUnchanged(),
            "Editing and moving a copied Control Box does not change the original Bend");
    }

    private static void RemoveUndo(bool wholeBend)
    {
        using var f = new Fixture(controlCount: 2);
        var before = State(f.Data);
        var target = wholeBend ? f.Bend : f.Data.Tree.FindSource(f.Controls[0])!.Id;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(TreeItemRemoval.Apply(f.Doc, f.Data, target, out var error) && f.Data.Tree.Find(target) is null && f.OriginalsUnchanged() &&
            f.Doc.Objects.FindId(f.Controls[0]) is { IsHidden: true } &&
            f.Doc.Objects.FindId(f.Controls[1])!.IsHidden == wholeBend &&
            f.Data.Tree.FindSource(f.Source)!.ParentId == (wholeBend ? null : f.Bend) &&
            f.Doc.Objects.FindId(f.Source) is { IsHidden: false } && f.Doc.NextUndoRecordSerialNumber == serial + 1,
            (wholeBend ? "Removing Bend" : "Removing one Control Box") + " hides only removed controls in place and retains input geometry: " + error);
        Check(f.Doc.Undo() && State(f.Data) == before && f.OriginalsUnchanged() && f.Controls.All(id => f.Doc.Objects.FindId(id) is { IsHidden: false }),
            "One removal Undo restores the exact Bend hierarchy and Control Box visibility");
    }

    private static Brep ExpectedResult(Fixture f)
    {
        using var evaluation = new TreeEvaluator();
        evaluation.Rebuild(f.Data.Tree, id => f.Doc.Objects.FindId(id)?.Geometry, f.Doc.ModelAbsoluteTolerance);
        return ResultGeometry.Create(evaluation.Find(f.Bend)!);
    }

    private static RhinoDoc NewDocument()
    {
        var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        doc.ModelAbsoluteTolerance = 0.001;
        return doc;
    }
    private static string State(TreeDocumentData data) => TreeStateCodec.Encode(data.Capture());
    private static bool ContainsBounds(Box box, BoundingBox bounds, double tolerance)
    {
        var local = Transform.PlaneToPlane(box.Plane, Plane.WorldXY);
        return bounds.GetCorners().All(point =>
        {
            point.Transform(local);
            return point.X >= box.X.Min - tolerance && point.X <= box.X.Max + tolerance &&
                point.Y >= box.Y.Min - tolerance && point.Y <= box.Y.Max + tolerance &&
                point.Z >= box.Z.Min - tolerance && point.Z <= box.Z.Max + tolerance;
        });
    }
    private static bool Near(double first, double second) => Math.Abs(first - second) < 1e-8;
    private static double Volume(Brep geometry) { using var mass = VolumeMassProperties.Compute(geometry); return mass!.Volume; }
    private static HashSet<Guid> ActiveIds(RhinoDoc doc) => doc.Objects.GetObjectList(new ObjectEnumeratorSettings
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
        public Guid Bend { get; }
        public Guid Source { get; }
        public List<Guid> Controls { get; } = [];
        private readonly Dictionary<Guid, GeometryBase> _originals = [];
        public IEnumerable<Guid> OriginalIds => _originals.Keys;

        public Fixture(int controlCount = 1)
        {
            var tree = new ModifierTreeModel();
            using var source = new BoundingBox(-5, 0, -4, 5, 100, 4).ToBrep();
            using var sourceAttributes = new ObjectAttributes { Name = "Input", ObjectColor = Color.CornflowerBlue, ColorSource = ObjectColorSource.ColorFromObject };
            Source = Doc.Objects.AddBrep(source, sourceAttributes);
            tree.RegisterSource(Source);
            Bend = tree.AddModifier(TreeNodeKind.Bend).Id;
            tree.RenameModifier(Bend, "Main bend", out _);
            tree.Move(tree.FindSource(Source)!.Id, Bend, 0, out _);
            for (var i = 0; i < controlCount; i++)
            {
                using var geometry = ControlBoxGeometry.Create(new Plane(new Point3d(0, 50, 0), Vector3d.XAxis, Vector3d.YAxis), new Vector3d(12, 110, 10));
                using var attributes = new ObjectAttributes { Name = $"Box {i + 1}", ObjectColor = Color.OrangeRed, ColorSource = ObjectColorSource.ColorFromObject };
                var id = Doc.Objects.AddBrep(geometry, attributes);
                if (!tree.AddControlBox(Bend, id, out var error)) throw new Exception(error);
                tree.SetControlBoxSettings(tree.FindSource(id)!.Id, new ControlBoxSettings(i == 0 ? 30 : -15, i == 0), out _);
                Controls.Add(id);
            }
            Doc.Groups.Add("Bend source and controls", Controls.Prepend(Source));
            foreach (var id in Controls.Prepend(Source)) _originals.Add(id, Doc.Objects.FindId(id)!.Geometry.Duplicate());
            Data = new TreeDocumentData(Doc);
            Data.Load(TreeStateCodec.Capture(tree, new InputVisibility(), true));
            Doc.Modified = false;
        }

        public bool OriginalsUnchanged() => _originals.All(pair =>
            Doc.Objects.FindId(pair.Key) is { } obj && GeometryBase.GeometryEquals(pair.Value, obj.Geometry));
        public void Dispose()
        {
            Data.Dispose();
            foreach (var geometry in _originals.Values) geometry.Dispose();
            Doc.Dispose();
        }
    }
}
