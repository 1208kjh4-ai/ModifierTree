using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class TreeStateCodecChecks
{
    public static void Verify()
    {
        var tree = new ModifierTreeModel();
        var visibility = new InputVisibility();
        var objectIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in objectIds) tree.RegisterSource(id);
        var sourceNodes = objectIds.Select(id => tree.FindSource(id)!).ToArray();
        var inner = tree.AddModifier();
        var outer = tree.AddModifier();
        var empty = tree.AddModifier();
        Move(tree, sourceNodes[0].Id, inner.Id, 0);
        Move(tree, sourceNodes[1].Id, inner.Id, 1);
        Move(tree, inner.Id, outer.Id, 0);
        Move(tree, sourceNodes[2].Id, outer.Id, 1);
        Move(tree, outer.Id, null, 0);
        visibility.Set(objectIds[1], true);
        visibility.Set(objectIds[3], true);
        var unregistered = Guid.NewGuid();
        visibility.Set(unregistered, true);
        var captured = TreeStateCodec.Capture(tree, visibility, previewEnabled: false);
        var encoded = TreeStateCodec.Encode(captured);
        var saved = TreeStateCodec.Decode(encoded);
        Check(TreeStateCodec.Encode(saved) == encoded && !saved.PreviewEnabled,
            "Versioned JSON round trip preserves a deterministic snapshot and result visibility");
        Check(saved.VisibleInputIds.ToHashSet().SetEquals(new[] { objectIds[1], objectIds[3] }),
            "Snapshot includes explicit source visibility and discards unregistered visibility remnants");

        tree.Remove(inner.Id);
        visibility.Set(objectIds[1], false);
        Check(captured.Nodes.Single(node => node.Id == inner.Id).Children.SequenceEqual(new[] { sourceNodes[0].Id, sourceNodes[1].Id }) &&
              captured.VisibleInputIds.Contains(objectIds[1]), "Captured state is detached from later tree and visibility edits");
        var beforeRevision = tree.Revision;
        TreeStateCodec.Apply(saved, tree, visibility);
        Check(tree.Revision > beforeRevision && tree.Find(inner.Id)?.Id == inner.Id && tree.FindSource(objectIds[0])?.Id == sourceNodes[0].Id,
            "Restore preserves node/source GUIDs in the existing model and invalidates cached evaluation");
        Check(tree.Roots.SequenceEqual(new[] { outer.Id, sourceNodes[3].Id, empty.Id }) &&
              tree.Find(outer.Id)!.Children.SequenceEqual(new[] { inner.Id, sourceNodes[2].Id }) &&
              tree.Find(inner.Id)!.Children.SequenceEqual(new[] { sourceNodes[0].Id, sourceNodes[1].Id }) &&
              tree.Find(inner.Id)!.ParentId == outer.Id && tree.Find(sourceNodes[1].Id)!.ParentId == inner.Id,
            "Nested operands, root order, parent links and empty Modifiers survive restore");
        Check(tree.SourceCount == 4 && tree.ModifierCount == 3 && visibility.IsVisible(objectIds[1]) &&
              !visibility.IsVisible(objectIds[0]) && !visibility.IsVisible(unregistered),
            "Restore rebuilds source registration and replaces visibility without retaining old entries");
        var scope = new TreeEditScope(tree);
        Check(scope.Enter(outer.Id) && scope.Candidates.SequenceEqual(new[] { inner.Id, sourceNodes[2].Id }),
            "Consumers retaining the model reference see the restored hierarchy");

        var detached = new ModifierTreeModel();
        TreeStateCodec.Apply(saved, detached, new InputVisibility());
        Check(detached.SourcesInSubtree(outer.Id).SequenceEqual(objectIds.Take(3)),
            "Restoration requires no Rhino objects and retains missing source GUIDs for later diagnosis");
        var emptyState = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, [], [], true, []);
        TreeStateCodec.Apply(TreeStateCodec.Decode(TreeStateCodec.Encode(emptyState)), detached, visibility);
        Check(!detached.Nodes.Any() && detached.Roots.Count == 0 && !visibility.IsVisible(objectIds[1]),
            "An empty snapshot clears both the tree and prior display settings");
        TreeStateCodec.Apply(saved, tree, visibility);

        void Reject(ModifierDocumentState invalid, string message)
        {
            var prior = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, false));
            var revision = tree.Revision;
            ExpectFormat(() => TreeStateCodec.Apply(invalid, tree, visibility));
            Check(tree.Revision == revision && TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, false)) == prior,
                message + " without changing existing tree or visibility");
        }

        Reject(Copy(saved, schemaVersion: TreeStateCodec.CurrentSchemaVersion + 1), "Unsupported future schema is rejected");
        Reject(Copy(saved, nodes: saved.Nodes.Append(saved.Nodes[0])), "Duplicate node GUID is rejected");
        var first = saved.Nodes.Single(node => node.Id == sourceNodes[0].Id);
        var second = saved.Nodes.Single(node => node.Id == sourceNodes[1].Id);
        Reject(Replace(saved, new ModifierNodeState(second.Id, second.Kind, first.ObjectId, second.ParentId, second.Children)),
            "Two registrations of the same source GUID are rejected");
        Reject(Replace(saved, new ModifierNodeState(first.Id, first.Kind, Guid.Empty, first.ParentId, first.Children)),
            "Empty source GUID is rejected");
        Reject(Copy(saved, roots: saved.Roots.Append(saved.Roots[0])), "Duplicate root location is rejected");
        var parent = saved.Nodes.Single(node => node.Id == outer.Id);
        Reject(Replace(saved, new ModifierNodeState(parent.Id, parent.Kind, null, parent.ParentId, parent.Children.Append(Guid.NewGuid()))),
            "Dangling child reference is rejected");
        Reject(Replace(saved, new ModifierNodeState(first.Id, first.Kind, first.ObjectId, outer.Id, [])),
            "Inconsistent child and parent links are rejected");
        Reject(Replace(saved, new ModifierNodeState(first.Id, first.Kind, first.ObjectId, Guid.NewGuid(), [])),
            "Dangling parent reference is rejected");
        Reject(Replace(saved, new ModifierNodeState(first.Id, first.Kind, first.ObjectId, first.ParentId, [empty.Id])),
            "Geometry with child nodes is rejected");
        Reject(Replace(saved, new ModifierNodeState(parent.Id, parent.Kind, Guid.NewGuid(), parent.ParentId, parent.Children)),
            "Modifier with a source GUID is rejected");
        Reject(Replace(saved, new ModifierNodeState(parent.Id, (TreeNodeKind)999, null, parent.ParentId, parent.Children)),
            "Unsupported Modifier kind is rejected");
        Reject(Copy(saved, visibleInputIds: [unregistered]), "Visibility for an unregistered input is rejected");
        Reject(Copy(saved, visibleInputIds: [objectIds[1], objectIds[1]]), "Duplicate visibility entry is rejected");
        Reject(Copy(saved, roots: []), "Unlocated root nodes are rejected");

        var cycleA = Guid.NewGuid();
        var cycleB = Guid.NewGuid();
        Reject(new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, [],
            [new ModifierNodeState(cycleA, TreeNodeKind.BooleanDifference, null, cycleB, [cycleB]),
             new ModifierNodeState(cycleB, TreeNodeKind.BooleanDifference, null, cycleA, [cycleA])], true, []),
            "A disconnected cycle with internally consistent links is rejected");
        var deepIds = Enumerable.Range(0, TreeStateCodec.MaxTreeDepth + 1).Select(_ => Guid.NewGuid()).ToArray();
        Reject(new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, [deepIds[0]], deepIds.Select((id, index) =>
            new ModifierNodeState(id, TreeNodeKind.BooleanDifference, null, index == 0 ? null : deepIds[index - 1],
                index + 1 < deepIds.Length ? [deepIds[index + 1]] : [])), true, []),
            "Excessive graph nesting is rejected before recursive consumers can run");
        var manyIds = Enumerable.Range(0, TreeStateCodec.MaxNodeCount + 1).Select(_ => Guid.NewGuid()).ToArray();
        Reject(new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, manyIds,
            manyIds.Select(id => new ModifierNodeState(id, TreeNodeKind.BooleanDifference, null, null, [])), true, []),
            "Oversized node collection is rejected");

        var corruptJson = new[]
        {
            "", "{", "null", "[]",
            encoded.Replace($"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion}", $"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion + 1}"),
            encoded.Replace($"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion}", "\"schemaVersion\":true"),
            encoded.Replace($"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion}", $"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion},\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion}"),
            encoded.Replace("BooleanDifference", "FutureModifier"),
            encoded.Replace(first.Id.ToString(), "bad-guid"),
            new string('x', TreeStateCodec.MaxJsonLength + 1)
        };
        foreach (var invalid in corruptJson) ExpectFormat(() => TreeStateCodec.Decode(invalid));
        var missingField = JsonNode.Parse(encoded)!.AsObject();
        missingField.Remove("previewEnabled");
        ExpectFormat(() => TreeStateCodec.Decode(missingField.ToJsonString()));
        Check(true, "Malformed, oversized, duplicate-field and unsupported JSON fail with FormatException");

        var roots = saved.Roots.ToArray();
        var children = parent.Children.ToArray();
        var copiedNode = new ModifierNodeState(parent.Id, parent.Kind, null, null, children);
        var copied = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, roots, [copiedNode], true, []);
        roots[0] = Guid.Empty;
        children[0] = Guid.Empty;
        Check(copied.Roots[0] == outer.Id && copied.Nodes[0].Children[0] == inner.Id &&
              copied.Roots is not Guid[] && copied.Nodes[0].Children is not Guid[],
            "Snapshot constructors copy caller collections and expose read-only data");
    }

    private static ModifierDocumentState Copy(ModifierDocumentState state, int? schemaVersion = null,
        IEnumerable<Guid>? roots = null, IEnumerable<ModifierNodeState>? nodes = null, IEnumerable<Guid>? visibleInputIds = null) =>
        new(schemaVersion ?? state.SchemaVersion, roots ?? state.Roots, nodes ?? state.Nodes, state.PreviewEnabled,
            visibleInputIds ?? state.VisibleInputIds);

    private static ModifierDocumentState Replace(ModifierDocumentState state, ModifierNodeState replacement) =>
        Copy(state, nodes: state.Nodes.Select(node => node.Id == replacement.Id ? replacement : node));

    private static void ExpectFormat(Action action)
    {
        try { action(); }
        catch (FormatException) { return; }
        throw new Exception("FAIL: Invalid snapshot did not throw FormatException.");
    }

    private static void Move(ModifierTreeModel tree, Guid id, Guid? parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
