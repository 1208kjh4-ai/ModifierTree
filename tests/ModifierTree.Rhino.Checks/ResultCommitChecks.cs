using System.Drawing;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

internal static class ResultCommitChecks
{
    public static void Run()
    {
        Bake();
        Merge();
        Rename();
        AttributesAndHiddenInputs();
        ExistingRecord();
        NotificationRollback();
        foreach (var reason in new[] { "missing", "empty", "locked", "readonly", "source" }) RejectBeforeWriting(reason);
    }

    private static void Bake()
    {
        using var fixture = new Fixture(nested: true);
        var before = Snapshot(fixture.Data);
        var count = fixture.Document.Objects.Count;
        Check(ResultCommit.Apply(fixture.Document, fixture.Data, fixture.Main, ResultCommitMode.Bake,
            out var resultNodeId, out var error), "Bake creates a fixed result from a nested named Modifier: " + error);
        var resultNode = fixture.Data.Tree.Find(resultNodeId)!;
        var result = fixture.Document.Objects.FindId(resultNode.ObjectId!.Value)!;
        Check(fixture.Document.Objects.Count == count + 1 && result is BrepObject &&
              result.Attributes.Name == "Main" && fixture.Data.Tree.Roots[0] == resultNodeId && resultNode.ParentId is null &&
              Near(Volume((Brep)result.Geometry), 500),
            "Bake adds exactly one independently named result at the first root position");
        var draft = new ModifierTreeModel();
        var visibility = new InputVisibility();
        TreeStateCodec.Apply(fixture.Data.Capture(), draft, visibility);
        draft.Remove(resultNodeId);
        Check(TreeStateCodec.Encode(TreeStateCodec.Capture(draft, visibility, fixture.Data.PreviewEnabled)) == before &&
              fixture.SourceIds.All(id => !fixture.Document.Objects.FindId(id)!.IsHidden),
            "Bake preserves the entire original tree, input settings and native source visibility");
        var resultObjectId = result.Id;
        Check(fixture.Document.Undo() && Snapshot(fixture.Data) == before &&
              fixture.Document.Objects.FindId(resultObjectId) is null &&
              fixture.SourceIds.All(id => fixture.Document.Objects.FindId(id) is not null),
            "One Bake Undo removes the fixed object and its tree row together");
    }

    private static void Merge()
    {
        using var fixture = new Fixture(nested: true);
        var before = Snapshot(fixture.Data);
        var count = fixture.Document.Objects.Count;
        var siblings = fixture.Data.Tree.Find(fixture.Parent!.Value)!.Children.ToArray();
        var previousVolume = EvaluateVolume(fixture, fixture.Parent.Value);
        Check(ResultCommit.Apply(fixture.Document, fixture.Data, fixture.Main, ResultCommitMode.Merge,
            out var resultNodeId, out var error), "Merge commits a nested Modifier result: " + error);
        var resultNode = fixture.Data.Tree.Find(resultNodeId)!;
        var resultId = resultNode.ObjectId!.Value;
        var children = fixture.Data.Tree.Find(fixture.Parent.Value)!.Children;
        Check(children.SequenceEqual(new[] { siblings[0], resultNodeId, siblings[2] }) &&
              resultNode.ParentId == fixture.Parent && fixture.Data.Tree.Find(fixture.Main) is null &&
              fixture.Data.Tree.FindSource(fixture.A) is null && fixture.Data.Tree.FindSource(fixture.B) is null,
            "Merge replaces the whole selected subtree at its exact parent and sibling position");
        Check(fixture.Document.Objects.Count == count + 1 && fixture.Document.Objects.FindId(resultId) is BrepObject &&
              fixture.Document.Objects.FindId(resultId)!.Attributes.Name == "Main" &&
              fixture.Document.Objects.FindId(fixture.A)!.IsHidden && fixture.Document.Objects.FindId(fixture.B)!.IsHidden &&
              fixture.SourceIds.Except(new[] { fixture.A, fixture.B }).All(id => !fixture.Document.Objects.FindId(id)!.IsHidden),
            "Merge creates one named object and hides only its retained original inputs");
        Check(Near(previousVolume, 679) && Near(EvaluateVolume(fixture, fixture.Parent.Value), previousVolume),
            "A parent Boolean retains the same evaluated solid after its child is merged");
        Check(fixture.Document.Undo() && Snapshot(fixture.Data) == before &&
              fixture.Document.Objects.FindId(resultId) is null &&
              fixture.SourceIds.All(id => fixture.Document.Objects.FindId(id) is { IsHidden: false }),
            "One Merge Undo restores the original subtree and native visibility and removes the replacement");
    }

    private static void Rename()
    {
        using var fixture = new Fixture();
        var tree = fixture.Data.Tree;
        var node = tree.Find(fixture.Main)!;
        var revision = tree.Revision;
        var before = Snapshot(fixture.Data);
        var structureNotifications = 0;
        fixture.Data.Changed += (_, e) => { if (e.StructureChanged) structureNotifications++; };
        using var evaluator = new TreeEvaluator();
        evaluator.Rebuild(tree, id => fixture.Document.Objects.FindId(id)?.Geometry, fixture.Document.ModelAbsoluteTolerance);
        var cached = evaluator.Find(fixture.Main);
        Check(fixture.Data.Edit("Rename Modifier", (draft, visibility) => draft.RenameModifier(fixture.Main, "  Main \uBCF8\uCCB4  ", out _), out _),
            "A Unicode Modifier name is committed through the native custom Undo record");
        Check(node.Name == "Main \uBCF8\uCCB4" && ModifierNames.DisplayName(node) == "(BD) Main \uBCF8\uCCB4" &&
              tree.Revision == revision && structureNotifications == 0 && ReferenceEquals(node, tree.Find(fixture.Main)) &&
              evaluator.IsSnapshotOf(tree, fixture.Document.ModelAbsoluteTolerance) && ReferenceEquals(cached, evaluator.Find(fixture.Main)),
            "Modifier renaming preserves node identity, geometry revision and the existing Boolean cache");
        Check(fixture.Document.Undo() && Snapshot(fixture.Data) == before && tree.Revision == revision &&
              node.Name == "Main" && structureNotifications == 0,
            "One native Undo restores the previous Modifier name without invalidating geometry");
    }

    private static void AttributesAndHiddenInputs()
    {
        using var fixture = new Fixture();
        var doc = fixture.Document;
        doc.Groups.Add("Original input group", new[] { fixture.A, fixture.B });
        using (var attributes = doc.Objects.FindId(fixture.A)!.Attributes.Duplicate())
        {
            attributes.ObjectColor = Color.CornflowerBlue;
            attributes.ColorSource = ObjectColorSource.ColorFromObject;
            doc.Objects.ModifyAttributes(fixture.A, attributes, true);
        }
        doc.Objects.Hide(fixture.B, true);
        var before = Snapshot(fixture.Data);
        Check(ResultCommit.Apply(doc, fixture.Data, fixture.Main, ResultCommitMode.Merge, out var resultNodeId, out var error),
            "Merge accepts an input already hidden in Rhino: " + error);
        var result = doc.Objects.FindId(fixture.Data.Tree.Find(resultNodeId)!.ObjectId!.Value)!;
        Check(result.Attributes.GroupCount == 0 && result.Attributes.Name == "Main" &&
              result.Attributes.ObjectColor.ToArgb() == Color.CornflowerBlue.ToArgb() && !result.IsHidden &&
              doc.Objects.FindId(fixture.A)!.Attributes.GroupCount == 1 && doc.Objects.FindId(fixture.B)!.IsHidden,
            "The fixed result keeps source appearance, starts visible, and does not inherit input group membership");
        Check(doc.Undo() && Snapshot(fixture.Data) == before && !doc.Objects.FindId(fixture.A)!.IsHidden &&
              doc.Objects.FindId(fixture.B)!.IsHidden,
            "Merge Undo restores a visible input while preserving a previously hidden input");
    }

    private static void ExistingRecord()
    {
        using var fixture = new Fixture();
        var doc = fixture.Document;
        var before = Snapshot(fixture.Data);
        var serial = doc.BeginUndoRecord("Host command containing Bake");
        Guid resultNodeId;
        try
        {
            Check(serial != 0 && ResultCommit.Apply(doc, fixture.Data, fixture.Main, ResultCommitMode.Bake,
                      out resultNodeId, out _) && doc.UndoRecordingIsActive && doc.CurrentUndoRecordSerialNumber == serial,
                "Bake joins a containing Rhino command record and leaves that record open");
        }
        finally { if (serial != 0) doc.EndUndoRecord(serial); }
        var addedIds = fixture.Data.Tree.Nodes.Where(node => node.ObjectId.HasValue)
            .Select(node => node.ObjectId!.Value).Except(fixture.SourceIds).ToArray();
        Check(addedIds.Length == 1 && doc.Undo() && Snapshot(fixture.Data) == before && doc.Objects.FindId(addedIds[0]) is null,
            "The containing Rhino command undoes both the baked object and custom tree state");
    }

    private static void NotificationRollback()
    {
        using var fixture = new Fixture();
        var before = Snapshot(fixture.Data);
        var notificationCount = 0;
        fixture.Data.Changed += (_, _) =>
        {
            if (++notificationCount == 1) throw new InvalidOperationException("Injected result notification failure");
        };
        fixture.Document.Modified = false;
        Check(!ResultCommit.Apply(fixture.Document, fixture.Data, fixture.Main, ResultCommitMode.Merge,
                  out var resultNodeId, out var error) && resultNodeId == Guid.Empty && error.Contains("Injected") &&
              Snapshot(fixture.Data) == before && !fixture.Document.Modified &&
              ActiveIds(fixture.Document).SetEquals(fixture.SourceIds) &&
              fixture.SourceIds.All(id => fixture.Document.Objects.FindId(id) is { IsHidden: false }),
            "A failure after applying tree state rolls back the result, input visibility and complete private snapshot");
        fixture.Document.Undo();
        Check(Snapshot(fixture.Data) == before && ActiveIds(fixture.Document).SetEquals(fixture.SourceIds) &&
              fixture.SourceIds.All(id => fixture.Document.Objects.FindId(id) is { IsHidden: false }),
            "Undo after a rolled-back result transaction does not resurrect an orphan object or partial tree");
    }

    private static void RejectBeforeWriting(string reason)
    {
        using var fixture = new Fixture();
        var doc = fixture.Document;
        if (reason == "missing") doc.Objects.Delete(fixture.B, true);
        if (reason == "locked") doc.Objects.Lock(fixture.B, true);
        if (reason == "readonly") fixture.Data.ReadOnlyReason = "Unsupported saved schema";
        if (reason == "empty")
        {
            using var enclosing = new BoundingBox(-1, -1, -1, 11, 11, 11).ToBrep();
            doc.Objects.Replace(fixture.B, enclosing);
        }
        var nodeId = reason == "source" ? fixture.Data.Tree.FindSource(fixture.A)!.Id : fixture.Main;
        var before = Snapshot(fixture.Data);
        var objects = ActiveIds(doc);
        var serial = doc.NextUndoRecordSerialNumber;
        doc.Modified = false;
        Check(!ResultCommit.Apply(doc, fixture.Data, nodeId, ResultCommitMode.Merge, out var resultNodeId, out var error) &&
              resultNodeId == Guid.Empty && error.Length > 0 && Snapshot(fixture.Data) == before &&
              ActiveIds(doc).SetEquals(objects) && serial == doc.NextUndoRecordSerialNumber && !doc.Modified,
            $"A {reason} result commit is rejected before changing native objects, the tree or Undo history");
    }

    private static string Snapshot(TreeDocumentData data) => TreeStateCodec.Encode(data.Capture());
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.01;
    private static double Volume(Brep brep)
    {
        using var mass = VolumeMassProperties.Compute(brep);
        return mass!.Volume;
    }

    private static double EvaluateVolume(Fixture fixture, Guid nodeId)
    {
        using var evaluation = new TreeEvaluator();
        evaluation.Rebuild(fixture.Data.Tree, id => fixture.Document.Objects.FindId(id)?.Geometry, fixture.Document.ModelAbsoluteTolerance);
        var result = evaluation.Find(nodeId);
        if (result is not { IsCurrent: true, HasResult: true }) throw new Exception("The fixture did not produce a current result.");
        return result.Results.Sum(Volume);
    }

    private static HashSet<Guid> ActiveIds(RhinoDoc document) => document.Objects.GetObjectList(new ObjectEnumeratorSettings
        { NormalObjects = true, HiddenObjects = true, LockedObjects = true, DeletedObjects = false }).Select(obj => obj.Id).ToHashSet();

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Document { get; } = RhinoDoc.CreateHeadless(null);
        public TreeDocumentData Data { get; }
        public Guid A { get; }
        public Guid B { get; }
        public Guid Main { get; }
        public Guid? Parent { get; }
        public List<Guid> SourceIds { get; } = [];

        public Fixture(bool nested = false)
        {
            Document.UndoRecordingEnabled = true;
            Document.ModelAbsoluteTolerance = 0.001;
            var tree = new ModifierTreeModel();
            Guid AddBox(double x0, double y0, double z0, double x1, double y1, double z1)
            {
                using var shape = new BoundingBox(x0, y0, z0, x1, y1, z1).ToBrep();
                var id = Document.Objects.AddBrep(shape);
                if (id == Guid.Empty) throw new Exception("Cannot create native result fixture.");
                SourceIds.Add(id);
                tree.RegisterSource(id);
                return id;
            }
            A = AddBox(0, 0, 0, 10, 10, 10);
            B = AddBox(5, -1, -1, 15, 11, 11);
            Main = tree.AddModifier().Id;
            tree.RenameModifier(Main, "Main", out _);
            tree.Move(tree.FindSource(A)!.Id, Main, 0, out _);
            tree.Move(tree.FindSource(B)!.Id, Main, 1, out _);
            if (nested)
            {
                // Cross the parent boundary: avoid the host's separate enclosed-cutter limitation.
                var outer = AddBox(-2, -2, -2, 3, 12, 12);
                var otherCutter = AddBox(-3, -3, -3, -1, -1, -1);
                Parent = tree.AddModifier().Id;
                tree.Move(tree.FindSource(outer)!.Id, Parent, 0, out _);
                tree.Move(Main, Parent, 1, out _);
                tree.Move(tree.FindSource(otherCutter)!.Id, Parent, 2, out _);
            }
            var visibility = new InputVisibility();
            visibility.Set(B, true);
            Data = new TreeDocumentData(Document);
            Data.Load(TreeStateCodec.Capture(tree, visibility, true));
        }

        public void Dispose() { Data.Dispose(); Document.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
