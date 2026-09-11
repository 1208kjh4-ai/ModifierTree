using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class ModifierDocumentOperationChecks
{
    public static void Run()
    {
        DuplicateAndUndo();
        DuplicateIndependence();
        DuplicateRollback();
        foreach (var reason in new[] { "missing", "readonly", "source" }) RejectedDuplicate(reason);
        EmptyDuplicate();
        ParameterUndo();
        ParameterValidation();
        EnabledUndo();
    }

    private static void DuplicateAndUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        Check(SubtreeDuplicate.Apply(f.Doc, f.Data, f.Mirror, out var copyId, out var error),
            "Duplicate creates a complete nested Modifier subtree: " + error);
        var copy = f.Data.Tree.Find(copyId)!;
        var copiedArray = f.Data.Tree.Find(copy.Children.Single())!;
        var copiedSources = f.Data.Tree.SourcesInSubtree(copyId);
        Check(f.Doc.NextUndoRecordSerialNumber == serial + 1 &&
              f.Data.Tree.Find(f.Parent)!.Children.SequenceEqual(new[] { f.Mirror, copyId, f.Standalone }) &&
              copy.Name == "Main" && copy.Mirror == f.Data.Tree.Find(f.Mirror)!.Mirror &&
              copiedArray.Name == "Row" && copiedArray.Array == f.Data.Tree.Find(f.Array)!.Array &&
              copiedSources.Count == 2 && !copiedSources.Intersect(f.SourceIds).Any(),
            "Duplicate preserves ordered settings and names with new identities in the next sibling slot and one Undo record");
        Check(ActiveIds(f.Doc).Count == f.SourceIds.Length + 2 && f.OriginalGeometryUnchanged() &&
              f.Data.InputVisibility.IsVisible(copiedSources[0]) && !f.Data.InputVisibility.IsVisible(copiedSources[1]) &&
              copiedSources.Select(id => f.Doc.Objects.FindId(id)!).All(obj => obj.Attributes.GroupCount == 0),
            "Duplicating retains original geometry and input-wire preferences while avoiding shared Rhino group membership");
        var archive = TreeArchive.Decode(TreeArchive.Create(f.Data.Capture()));
        Check(archive.State is not null && TreeStateCodec.Encode(archive.State) == State(f.Data),
            "Native archive dictionary preserves duplicated Mirror and Array settings and independent source links");
        Check(f.Doc.Undo() && State(f.Data) == before && ActiveIds(f.Doc).SetEquals(f.SourceIds) && f.OriginalGeometryUnchanged(),
            "One Duplicate Undo removes every copied source and restores the complete original tree");
    }

    private static void DuplicateIndependence()
    {
        using var f = new Fixture();
        f.Doc.Objects.Hide(f.SourceIds[1], true);
        Check(SubtreeDuplicate.Apply(f.Doc, f.Data, f.Mirror, out var copyId, out _),
            "Duplicate accepts hidden source inputs without changing their visibility");
        var copied = f.Data.Tree.SourcesInSubtree(copyId);
        var copiedArray = f.Data.Tree.Find(copyId)!.Children.Single();
        var originalSettings = f.Data.Tree.Find(f.Array)!.Array;
        Check(copied.All(id => f.Doc.Objects.FindId(id) is { IsHidden: false, IsLocked: false }) &&
              f.Doc.Objects.FindId(f.SourceIds[1]) is { IsHidden: true },
            "Copied source inputs start editable while the original hidden input remains hidden");
        f.Doc.Objects.Transform(copied[0], Transform.Translation(50, 0, 0), true);
        Check(ModifierPropertyEdit.Apply(f.Data, copiedArray, "Independent", true, null,
                  new ArraySettings { CountX = 3, Spacing = new ModifierVector(7, 0, 0) }, out _) &&
              f.Data.Tree.Find(f.Array)!.Array == originalSettings && f.Data.Tree.Find(f.Array)!.Name == "Row" &&
              f.OriginalGeometryUnchanged(),
            "Moving a copied source and editing copied Array properties leaves original geometry and settings independent");
    }

    private static void DuplicateRollback()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var notifications = 0;
        f.Data.Changed += (_, _) => { if (++notifications == 1) throw new InvalidOperationException("Injected duplicate failure"); };
        Check(!SubtreeDuplicate.Apply(f.Doc, f.Data, f.Mirror, out var copyId, out var error) &&
              copyId == Guid.Empty && error.Contains("Injected") && State(f.Data) == before &&
              ActiveIds(f.Doc).SetEquals(f.SourceIds) && f.OriginalGeometryUnchanged() && !f.Doc.Modified,
            "A notification failure rolls back copied objects and private tree state together");
        f.Doc.Undo();
        Check(State(f.Data) == before && ActiveIds(f.Doc).SetEquals(f.SourceIds),
            "Undo after a failed Duplicate does not restore orphan copied sources");
    }

    private static void RejectedDuplicate(string reason)
    {
        using var f = new Fixture();
        if (reason == "missing") f.Doc.Objects.Delete(f.SourceIds[0], true);
        if (reason == "readonly") f.Data.ReadOnlyReason = "Unsupported future schema";
        var selected = reason == "source" ? f.Data.Tree.FindSource(f.SourceIds[0])!.Id : f.Mirror;
        var before = State(f.Data);
        var ids = ActiveIds(f.Doc);
        var serial = f.Doc.NextUndoRecordSerialNumber;
        f.Doc.Modified = false;
        Check(!SubtreeDuplicate.Apply(f.Doc, f.Data, selected, out var copyId, out var error) &&
              copyId == Guid.Empty && error.Length > 0 && State(f.Data) == before && ids.SetEquals(ActiveIds(f.Doc)) &&
              serial == f.Doc.NextUndoRecordSerialNumber && !f.Doc.Modified,
            $"Duplicate rejects a {reason} target before changing objects, tree or Undo history");
    }

    private static void EmptyDuplicate()
    {
        using var doc = RhinoDoc.CreateHeadless(null);
        doc.UndoRecordingEnabled = true;
        using var data = new TreeDocumentData(doc);
        var model = new ModifierTreeModel();
        var original = model.AddModifier(TreeNodeKind.Array).Id;
        data.Load(TreeStateCodec.Capture(model, new InputVisibility(), true));
        Check(SubtreeDuplicate.Apply(doc, data, original, out var copy, out _) &&
              data.Tree.Roots.SequenceEqual(new[] { original, copy }) && data.Tree.SourceCount == 0 &&
              doc.Objects.Count == 0 && data.Tree.Find(copy)!.Array == data.Tree.Find(original)!.Array,
            "An empty Modifier can be duplicated without requiring a computed result or creating native objects");
    }

    private static void ParameterUndo()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var revision = f.Data.Tree.Revision;
        var geometryEvents = 0;
        var structureEvents = 0;
        f.Data.Changed += (_, e) => { if (e.GeometryChanged) geometryEvents++; if (e.StructureChanged) structureEvents++; };
        var settings = new MirrorSettings(new ModifierVector(8, 2, 0), new ModifierVector(0, 1, 0), true);
        Check(ModifierPropertyEdit.Apply(f.Data, f.Mirror, "새 이름", false, settings, null, out _) &&
              f.Data.Tree.Find(f.Mirror) is { Enabled: false, Name: "새 이름" } changed && changed.Mirror == settings &&
              f.Data.Tree.Revision > revision && geometryEvents == 1 && structureEvents == 0 && f.OriginalGeometryUnchanged(),
            "One properties transaction applies name, enabled state and Mirror plane while invalidating geometry without changing hierarchy");
        Check(f.Doc.Undo() && State(f.Data) == before && geometryEvents == 2 && structureEvents == 0 && f.OriginalGeometryUnchanged(),
            "One properties Undo restores every field and invalidates preview without replacing source geometry");
    }

    private static void ParameterValidation()
    {
        using var f = new Fixture();
        var before = State(f.Data);
        var revision = f.Data.Tree.Revision;
        var serial = f.Doc.NextUndoRecordSerialNumber;
        var node = f.Data.Tree.Find(f.Mirror)!;
        Check(ModifierPropertyEdit.Apply(f.Data, f.Mirror, node.Name, node.Enabled, node.Mirror, null, out _) &&
              !f.Doc.Modified && f.Doc.NextUndoRecordSerialNumber == serial && f.Data.Tree.Revision == revision,
            "Applying unchanged Modifier properties creates no Undo or geometry rebuild");
        Check(!ModifierPropertyEdit.Apply(f.Data, f.Mirror, "Should not persist", false,
                  new MirrorSettings(new ModifierVector(0, 0, 0), new ModifierVector(0, 0, 0)), null, out var error) &&
              error.Length > 0 && State(f.Data) == before && f.Doc.NextUndoRecordSerialNumber == serial && !f.Doc.Modified,
            "Invalid plane parameters reject the entire edit including its name and ON/OFF changes");
        Check(!ModifierPropertyEdit.Apply(f.Data, f.Array, "Wrong kind", true, node.Mirror, null, out _) &&
              State(f.Data) == before && f.OriginalGeometryUnchanged(),
            "Properties for the wrong Modifier kind cannot overwrite the tree");
    }

    private static void EnabledUndo()
    {
        using var f = new Fixture();
        using var evaluation = new TreeEvaluator();
        evaluation.Rebuild(f.Data.Tree, id => f.Doc.Objects.FindId(id)?.Geometry, f.Doc.ModelAbsoluteTolerance);
        Check(ModifierPropertyEdit.SetEnabled(f.Data, f.Mirror, false, out _) &&
              !evaluation.IsSnapshotOf(f.Data.Tree, f.Doc.ModelAbsoluteTolerance),
            "ON/OFF invalidates a previously evaluated Modifier tree");
        Check(f.Doc.Undo() && f.Data.Tree.Find(f.Mirror)!.Enabled && f.OriginalGeometryUnchanged(),
            "ON/OFF is restored by a single native Undo without touching source geometry");
    }

    private static string State(TreeDocumentData data) => TreeStateCodec.Encode(data.Capture());
    private static HashSet<Guid> ActiveIds(RhinoDoc doc) => doc.Objects.GetObjectList(new ObjectEnumeratorSettings
        { NormalObjects = true, HiddenObjects = true, LockedObjects = true, DeletedObjects = false }).Select(obj => obj.Id).ToHashSet();
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Doc { get; } = RhinoDoc.CreateHeadless(null);
        public TreeDocumentData Data { get; }
        public Guid[] SourceIds { get; }
        public Guid Parent { get; }
        public Guid Mirror { get; }
        public Guid Array { get; }
        public Guid Standalone { get; }
        private readonly Dictionary<Guid, GeometryBase> _originals = [];

        public Fixture()
        {
            Doc.UndoRecordingEnabled = true;
            Doc.ModelAbsoluteTolerance = 0.001;
            var tree = new ModifierTreeModel();
            var group = Doc.Groups.Add("Original sources");
            foreach (var x in new[] { 1d, 1.5, 8d })
            {
                using var box = new BoundingBox(x, 0, 0, x + 1, 1, 1).ToBrep();
                using var attrs = new ObjectAttributes { Name = "원본 " + x };
                attrs.AddToGroup(group);
                var id = Doc.Objects.AddBrep(box, attrs);
                if (id == Guid.Empty) throw new Exception("Fixture add failed.");
                _originals.Add(id, Doc.Objects.FindId(id)!.Geometry.Duplicate());
                tree.RegisterSource(id);
            }
            SourceIds = _originals.Keys.ToArray();
            Parent = tree.AddModifier(TreeNodeKind.BooleanUnion).Id;
            Mirror = tree.AddModifier(TreeNodeKind.Mirror).Id;
            Array = tree.AddModifier(TreeNodeKind.Array).Id;
            tree.RenameModifier(Mirror, "Main", out _);
            tree.RenameModifier(Array, "Row", out _);
            tree.SetMirrorSettings(Mirror, new MirrorSettings(new ModifierVector(3, 0, 0), new ModifierVector(1, 0, 0), false), out _);
            tree.SetArraySettings(Array, new ArraySettings { CountX = 2, Spacing = new ModifierVector(4, 0, 0) }, out _);
            tree.Move(Mirror, Parent, 0, out _);
            tree.Move(Array, Mirror, 0, out _);
            tree.Move(tree.FindSource(SourceIds[0])!.Id, Array, 0, out _);
            tree.Move(tree.FindSource(SourceIds[1])!.Id, Array, 1, out _);
            Standalone = tree.FindSource(SourceIds[2])!.Id;
            tree.Move(Standalone, Parent, 1, out _);
            var visibility = new InputVisibility();
            visibility.Set(SourceIds[0], true);
            Data = new TreeDocumentData(Doc);
            Data.Load(TreeStateCodec.Capture(tree, visibility, true));
            Doc.Modified = false;
        }

        public bool OriginalGeometryUnchanged() => _originals.All(pair =>
            Doc.Objects.FindId(pair.Key) is { } obj && GeometryBase.GeometryEquals(obj.Geometry, pair.Value));
        public void Dispose()
        {
            Data.Dispose();
            foreach (var geometry in _originals.Values) geometry.Dispose();
            Doc.Dispose();
        }
    }
}
