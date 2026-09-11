using ModifierTree.Core;

internal static class ModifierTreeChecks
{
    public static void Verify()
    {
        var tree = new ModifierTreeModel();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        tree.RegisterSource(aId);
        tree.RegisterSource(bId);
        var a = tree.FindSource(aId)!;
        var b = tree.FindSource(bId)!;
        var modifier = tree.AddModifier();
        Check(modifier.Children.Count == 0 && tree.Roots.SequenceEqual(new[] { a.Id, b.Id, modifier.Id }),
            "Add Modifier creates an empty root without adopting existing objects");
        Check(!tree.RegisterSource(aId) && tree.FindSource(aId)!.Id == a.Id, "Re-registering retains node identity and placement");
        Move(tree, a.Id, modifier.Id, 0);
        Move(tree, b.Id, modifier.Id, 1);
        Check(modifier.Children.SequenceEqual(new[] { a.Id, b.Id }) && tree.Roots.SequenceEqual(new[] { modifier.Id }),
            "Drop onto Modifier makes ordered children with one location per source");
        Move(tree, a.Id, modifier.Id, 2);
        Check(modifier.Children.SequenceEqual(new[] { b.Id, a.Id }), "Moving downward adjusts the pre-removal insertion index");
        Move(tree, a.Id, modifier.Id, 0);
        Check(modifier.Children.SequenceEqual(new[] { a.Id, b.Id }), "Moving upward restores the original operand order");
        var revision = tree.Revision;
        Move(tree, a.Id, modifier.Id, 1);
        Check(tree.Revision == revision, "Dropping at the same position is a no-op");
        Check(!tree.Move(a.Id, b.Id, 0, out _) && tree.Revision == revision, "Geometry cannot become a parent");
        Check(!tree.Move(modifier.Id, modifier.Id, 0, out _) && tree.Revision == revision, "Self-parenting is rejected atomically");
        var parent = tree.AddModifier();
        Move(tree, modifier.Id, parent.Id, 0);
        Check(!tree.Move(parent.Id, modifier.Id, 0, out _), "Moving an ancestor into its descendant is rejected");
        Check(!tree.Move(b.Id, parent.Id, 99, out _) && b.ParentId == modifier.Id, "Invalid insertion index does not detach the node");
        Move(tree, b.Id, parent.Id, 1);
        Check(modifier.Children.SequenceEqual(new[] { a.Id }) && parent.Children.SequenceEqual(new[] { modifier.Id, b.Id }),
            "Moving to another Modifier updates both old and new input lists");
        Move(tree, b.Id, null, tree.Roots.Count);
        Check(b.ParentId is null && tree.Roots.Contains(b.Id), "Dropping at root disconnects the input but retains registration");
        tree.Remove(modifier.Id);
        Check(a.ParentId == parent.Id && parent.Children.SequenceEqual(new[] { a.Id }), "Removing a nested Modifier promotes its children in place");
        tree.Remove(parent.Id);
        Check(a.ParentId is null && tree.FindSource(aId) is not null && tree.FindSource(bId) is not null,
            "Removing a root Modifier preserves all registered sources");
        Move(tree, b.Id, null, 0);
        Check(tree.Roots.SequenceEqual(new[] { b.Id, a.Id }), "Root order is user-controlled");
        tree.Remove(a.Id);
        Check(tree.FindSource(aId) is null && tree.FindSource(bId) is not null, "Unregistering removes only that node");
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
