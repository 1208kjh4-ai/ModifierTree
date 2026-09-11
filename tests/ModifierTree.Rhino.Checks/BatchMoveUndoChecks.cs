using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.Geometry;

internal static class BatchMoveUndoChecks
{
    public static void Run()
    {
        MoveAndUndo();
        RepeatedDropDoesNotRecord();
        InvalidBatchDoesNotRecord();
    }

    private static void MoveAndUndo()
    {
        using var fixture = new Fixture();
        var doc = fixture.Document;
        var data = fixture.Data;
        var before = TreeStateCodec.Encode(data.Capture());
        var serial = doc.NextUndoRecordSerialNumber;
        var changes = 0;
        data.Changed += (_, _) => changes++;

        // The first child's selected ancestor must carry it without detaching it;
        // another selected source is taken from a different parent in the same edit.
        Check(fixture.Drop() && changes == 1 && doc.Modified && doc.NextUndoRecordSerialNumber == serial + 1 &&
              data.Tree.Find(fixture.Target)!.Children.SequenceEqual(new[] { fixture.Nodes[3], fixture.Left, fixture.Nodes[2] }) &&
              data.Tree.Find(fixture.Left)!.Children.SequenceEqual(new[] { fixture.Nodes[0], fixture.Nodes[1] }) &&
              data.Tree.Find(fixture.Right)!.Children.Count == 0,
            "One native Undo record moves an entire multi-parent selection while retaining a selected ancestor's children");
        Check(fixture.NativeGeometryUnchanged(),
            "Batch tree movement preserves every native source GUID, geometry and object count");
        Check(doc.Undo() && changes == 2 && TreeStateCodec.Encode(data.Capture()) == before && fixture.NativeGeometryUnchanged(),
            "One native Undo restores the complete batch's original parents, order and stable node IDs without touching source geometry");
    }

    private static void RepeatedDropDoesNotRecord()
    {
        using var fixture = new Fixture();
        if (!fixture.Drop()) throw new Exception("FAIL: Could not prepare repeated batch drop fixture.");
        fixture.Document.Modified = false;
        var serial = fixture.Document.NextUndoRecordSerialNumber;
        var revision = fixture.Data.Tree.Revision;
        var before = TreeStateCodec.Encode(fixture.Data.Capture());
        var changes = 0;
        fixture.Data.Changed += (_, _) => changes++;
        var first = fixture.Drop();
        var second = fixture.Drop();
        Check(!first && !second && !fixture.Document.Modified && changes == 0 &&
              fixture.Document.NextUndoRecordSerialNumber == serial && fixture.Data.Tree.Revision == revision &&
              TreeStateCodec.Encode(fixture.Data.Capture()) == before && fixture.NativeGeometryUnchanged(),
            "Repeated no-op batch drops create no additional Undo record, document change or tree refresh");
    }

    private static void InvalidBatchDoesNotRecord()
    {
        using var fixture = new Fixture();
        var data = fixture.Data;
        var serial = fixture.Document.NextUndoRecordSerialNumber;
        var revision = data.Tree.Revision;
        var before = TreeStateCodec.Encode(data.Capture());
        var changes = 0;
        data.Changed += (_, _) => changes++;
        var cycle = data.Edit("Invalid cycle batch", (tree, visibility) =>
            tree.MoveMany([fixture.Nodes[3], fixture.Left, fixture.Nodes[0]], fixture.Left, 0, out _), out _);
        var missing = data.Edit("Stale batch", (tree, visibility) =>
            tree.MoveMany([fixture.Nodes[2], Guid.NewGuid()], fixture.Target, 0, out _), out _);
        var badPosition = data.Edit("Invalid batch position", (tree, visibility) =>
            tree.MoveMany([fixture.Nodes[1], fixture.Nodes[2]], fixture.Target, 99, out _), out _);
        Check(!cycle && !missing && !badPosition && changes == 0 && !fixture.Document.Modified &&
              fixture.Document.NextUndoRecordSerialNumber == serial && data.Tree.Revision == revision &&
              TreeStateCodec.Encode(data.Capture()) == before && fixture.NativeGeometryUnchanged(),
            "Invalid multi-node drops leave the entire private tree and native document unchanged without reserving Undo");
    }

    private sealed class Fixture : IDisposable
    {
        public RhinoDoc Document { get; } = RhinoDoc.CreateHeadless(null);
        public TreeDocumentData Data { get; }
        public Guid[] Nodes { get; }
        public Guid Left { get; }
        public Guid Right { get; }
        public Guid Target { get; }
        private readonly Dictionary<Guid, GeometryBase> _originals = [];

        public Fixture()
        {
            Document.UndoRecordingEnabled = true;
            var tree = new ModifierTreeModel();
            for (var index = 0; index < 4; index++)
            {
                using var shape = new BoundingBox(index * 20, 0, 0, index * 20 + 10, 10, 10).ToBrep();
                var id = Document.Objects.AddBrep(shape);
                if (id == Guid.Empty) throw new Exception("FAIL: Could not create batch Undo fixture geometry.");
                // AddBrep can normalize native geometry before any tree edit occurs.
                // Compare against the document-owned baseline, retaining strict geometry equality.
                var snapshot = Document.Objects.FindId(id)?.Geometry.Duplicate()
                    ?? throw new Exception("FAIL: Could not snapshot batch Undo fixture geometry.");
                _originals.Add(id, snapshot);
                tree.RegisterSource(id);
            }
            Nodes = _originals.Keys.Select(id => tree.FindSource(id)!.Id).ToArray();
            Left = tree.AddModifier().Id;
            Right = tree.AddModifier().Id;
            Target = tree.AddModifier(TreeNodeKind.BooleanUnion).Id;
            tree.MoveMany([Nodes[0], Nodes[1]], Left, 0, out _);
            tree.MoveMany([Nodes[2]], Right, 0, out _);
            tree.MoveMany([Nodes[3]], Target, 0, out _);
            var visibility = new InputVisibility();
            visibility.Set(_originals.Keys.Last(), true);
            Data = new TreeDocumentData(Document);
            Data.Load(TreeStateCodec.Capture(tree, visibility, true));
            if (!NativeGeometryUnchanged())
                throw new Exception("FAIL: Batch Undo fixture geometry differs before the first tree edit.");
            Document.Modified = false;
        }

        public bool Drop() => Data.Edit("Move tree items", (tree, visibility) =>
            tree.MoveMany([Nodes[2], Left, Nodes[0]], Target, tree.ChildrenOf(Target).Count, out _), out _);

        public bool NativeGeometryUnchanged() => Document.Objects.Count == _originals.Count &&
            _originals.All(pair => Document.Objects.FindId(pair.Key) is { } source &&
                GeometryBase.GeometryEquals(source.Geometry, pair.Value));

        public void Dispose()
        {
            Data.Dispose();
            foreach (var shape in _originals.Values) shape.Dispose();
            Document.Dispose();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
