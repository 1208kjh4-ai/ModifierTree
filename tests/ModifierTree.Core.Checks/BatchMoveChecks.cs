using ModifierTree.Core;

internal static class BatchMoveChecks
{
    public static void Verify()
    {
        RootAndSiblingOrder();
        CrossParentAndNormalization();
        RejectedMovesAreAtomic();
    }

    private static void RootAndSiblingOrder()
    {
        var tree = new ModifierTreeModel();
        var nodes = AddSources(tree, 5);
        var modifier = tree.AddModifier();
        var revision = tree.Revision;
        Check(tree.CanMoveMany([nodes[3].Id, nodes[1].Id], modifier.Id, 0, out _) && tree.Revision == revision &&
              modifier.Children.Count == 0, "Batch drop validation does not change the tree");
        MoveMany(tree, [nodes[3].Id, nodes[1].Id, nodes[3].Id], modifier.Id, 0);
        Check(modifier.Children.SequenceEqual(new[] { nodes[1].Id, nodes[3].Id }) &&
              tree.Roots.SequenceEqual(new[] { nodes[0].Id, nodes[2].Id, nodes[4].Id, modifier.Id }) &&
              nodes[1].ParentId == modifier.Id && nodes[3].ParentId == modifier.Id && tree.Revision == revision + 1,
            "Noncontiguous root selection adopts all inputs once, in visual order rather than selection order");
        MoveMany(tree, [nodes[4].Id, nodes[2].Id, nodes[0].Id], modifier.Id, 1);
        Check(modifier.Children.SequenceEqual(new[] { nodes[1].Id, nodes[0].Id, nodes[2].Id, nodes[4].Id, nodes[3].Id }),
            "Batch insertion between existing inputs keeps the selected block in top-to-bottom order");

        revision = tree.Revision;
        MoveMany(tree, [nodes[2].Id, nodes[1].Id], modifier.Id, 5);
        Check(modifier.Children.SequenceEqual(new[] { nodes[0].Id, nodes[4].Id, nodes[3].Id, nodes[1].Id, nodes[2].Id }) &&
              tree.Revision == revision + 1,
            "Downward batch reorder adjusts the insertion index for every selected preceding sibling");
        MoveMany(tree, [nodes[2].Id, nodes[1].Id], modifier.Id, 0);
        Check(modifier.Children.SequenceEqual(new[] { nodes[1].Id, nodes[2].Id, nodes[0].Id, nodes[4].Id, nodes[3].Id }),
            "Upward batch reorder preserves the selected block order");

        revision = tree.Revision;
        foreach (var index in new[] { 0, 1, 2 }) MoveMany(tree, [nodes[2].Id, nodes[1].Id], modifier.Id, index);
        MoveMany(tree, modifier.Children.Reverse(), modifier.Id, modifier.Children.Count);
        Check(tree.Revision == revision && modifier.Children.SequenceEqual(
                new[] { nodes[1].Id, nodes[2].Id, nodes[0].Id, nodes[4].Id, nodes[3].Id }),
            "Dropping a contiguous block on itself or moving every sibling in place is a no-op");

        MoveMany(tree, [nodes[3].Id, nodes[1].Id], null, 0);
        Check(tree.Roots.SequenceEqual(new[] { nodes[1].Id, nodes[3].Id, modifier.Id }) &&
              nodes[1].ParentId is null && nodes[3].ParentId is null &&
              modifier.Children.SequenceEqual(new[] { nodes[2].Id, nodes[0].Id, nodes[4].Id }),
            "Multiple inputs can be promoted together to the root at the requested position");
        Check(nodes.All(node => ReferenceEquals(tree.Find(node.Id), node) && ReferenceEquals(tree.FindSource(node.ObjectId!.Value), node)),
            "Batch moves preserve all node instances and native source identities");
    }

    private static void CrossParentAndNormalization()
    {
        var tree = new ModifierTreeModel();
        var nodes = AddSources(tree, 5);
        var left = tree.AddModifier();
        var right = tree.AddModifier();
        var target = tree.AddModifier();
        MoveMany(tree, [nodes[0].Id, nodes[1].Id], left.Id, 0);
        MoveMany(tree, [nodes[2].Id, nodes[3].Id], right.Id, 0);
        MoveMany(tree, [nodes[4].Id], target.Id, 0);
        var normalized = tree.NormalizeMoveNodes([nodes[3].Id, left.Id, nodes[0].Id, left.Id, nodes[2].Id]);
        Check(normalized.SequenceEqual(new[] { left.Id, nodes[2].Id, nodes[3].Id }),
            "Selection normalization removes duplicates and selected descendants while retaining visual preorder");

        var revision = tree.Revision;
        MoveMany(tree, [nodes[3].Id, nodes[1].Id, nodes[4].Id], target.Id, 1);
        Check(left.Children.SequenceEqual(new[] { nodes[0].Id }) && right.Children.SequenceEqual(new[] { nodes[2].Id }) &&
              target.Children.SequenceEqual(new[] { nodes[1].Id, nodes[3].Id, nodes[4].Id }) &&
              tree.Revision == revision + 1,
            "One batch can combine inputs from multiple parents and the destination itself with one revision");

        revision = tree.Revision;
        MoveMany(tree, [nodes[0].Id, left.Id], target.Id, 0);
        Check(left.ParentId == target.Id && nodes[0].ParentId == left.Id && left.Children.SequenceEqual(new[] { nodes[0].Id }) &&
              target.Children.SequenceEqual(new[] { left.Id, nodes[1].Id, nodes[3].Id, nodes[4].Id }) && tree.Revision == revision + 1,
            "Dragging an ancestor and its selected child moves the subtree intact without extracting the child");
        var state = TreeStateCodec.Capture(tree, new InputVisibility(), true);
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(TreeStateCodec.Decode(TreeStateCodec.Encode(state)), restored, new InputVisibility());
        Check(restored.Find(left.Id)!.ParentId == target.Id && restored.Find(nodes[0].Id)!.ParentId == left.Id &&
              restored.Find(target.Id)!.Children.SequenceEqual(target.Children),
            "Batch hierarchy edits round-trip with unchanged IDs, nesting and operand order");
    }

    private static void RejectedMovesAreAtomic()
    {
        var tree = new ModifierTreeModel();
        var nodes = AddSources(tree, 2);
        var outer = tree.AddModifier();
        var inner = tree.AddModifier();
        MoveMany(tree, [inner.Id], outer.Id, 0);
        MoveMany(tree, [nodes[0].Id], inner.Id, 0);
        var revision = tree.Revision;
        var before = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), true));
        var missing = Guid.NewGuid();
        var rejected = new (Guid[] Ids, Guid? Parent, int Index)[]
        {
            ([nodes[1].Id, outer.Id], inner.Id, 0),
            ([nodes[1].Id, inner.Id, nodes[0].Id], inner.Id, 0),
            ([nodes[1].Id, missing], outer.Id, 0),
            ([outer.Id, missing], null, 0),
            ([nodes[1].Id], nodes[0].Id, 0),
            ([nodes[1].Id], missing, 0),
            ([nodes[0].Id, nodes[1].Id], outer.Id, -1),
            ([nodes[0].Id, nodes[1].Id], outer.Id, 2),
            ([], outer.Id, 0)
        };
        foreach (var (ids, parent, index) in rejected)
        {
            if (tree.CanMoveMany(ids, parent, index, out _) || tree.MoveMany(ids, parent, index, out var error) || error.Length == 0)
                throw new Exception("FAIL: An invalid batch move was accepted.");
        }
        Check(tree.Revision == revision && TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), true)) == before,
            "Cycles, missing selected nodes or parents, geometry parents, invalid indexes and empty batches reject the whole edit atomically");
        try
        {
            tree.NormalizeMoveNodes([outer.Id, missing]);
            throw new Exception("FAIL: Normalization ignored a missing selected node.");
        }
        catch (ArgumentException) { }
        Check(tree.NormalizeMoveNodes([]).Count == 0 && tree.Revision == revision,
            "Normalization accepts an empty selection and explicitly rejects stale selected IDs");
    }

    private static ModifierTreeNode[] AddSources(ModifierTreeModel tree, int count)
    {
        return Enumerable.Range(0, count).Select(_ =>
        {
            var id = Guid.NewGuid();
            tree.RegisterSource(id);
            return tree.FindSource(id)!;
        }).ToArray();
    }

    private static void MoveMany(ModifierTreeModel tree, IEnumerable<Guid> ids, Guid? parent, int index)
    {
        if (!tree.MoveMany(ids, parent, index, out var error)) throw new Exception(error);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
