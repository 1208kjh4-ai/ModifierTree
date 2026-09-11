using ModifierTree.Core;

internal static class SubtreeChecks
{
    public static void Verify()
    {
        var tree = new ModifierTreeModel();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        tree.RegisterSource(a); tree.RegisterSource(b); tree.RegisterSource(c);
        var root = tree.AddModifier(); var nested = tree.AddModifier();
        tree.Move(nested.Id, root.Id, 0, out _);
        tree.Move(tree.FindSource(a)!.Id, nested.Id, 0, out _);
        tree.Move(tree.FindSource(b)!.Id, nested.Id, 1, out _);
        tree.Move(tree.FindSource(c)!.Id, root.Id, 1, out _);
        Check(tree.SourcesInSubtree(root.Id).SequenceEqual(new[] { a, b, c }), "Root move owns all descendant sources exactly once");
        Check(tree.SourcesInSubtree(nested.Id).SequenceEqual(new[] { a, b }), "Subtree move excludes sibling sources");
        Check(tree.SourcesInSubtree(tree.FindSource(b)!.Id).SequenceEqual(new[] { b }), "Geometry move owns only that source");
        Check(tree.SourcesInSubtree(tree.AddModifier().Id).Count == 0 && tree.SourcesInSubtree(Guid.NewGuid()).Count == 0,
            "Empty and missing nodes have no move targets");
        var visibility = new InputVisibility();
        Check(!visibility.IsVisible(a) && !visibility.IsVisible(b), "Input wires start hidden");
        visibility.Set(b, true);
        tree.Move(tree.FindSource(b)!.Id, root.Id, 0, out _);
        Check(visibility.IsVisible(b) && !visibility.IsVisible(a), "Explicit source visibility survives reparenting and role changes");
        visibility.Set(b, false);
        Check(!visibility.IsVisible(b), "Hiding a source clears its explicit wire visibility");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
