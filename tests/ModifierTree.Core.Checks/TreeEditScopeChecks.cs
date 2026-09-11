using ModifierTree.Core;

internal static class TreeEditScopeChecks
{
    public static void Verify()
    {
        var tree = new ModifierTreeModel();
        var root = tree.AddModifier(); var child = tree.AddModifier(); var other = tree.AddModifier();
        var objectId = Guid.NewGuid(); tree.RegisterSource(objectId);
        var source = tree.FindSource(objectId)!;
        tree.Move(child.Id, root.Id, 0, out _); tree.Move(source.Id, child.Id, 0, out _);
        var scope = new TreeEditScope(tree);
        Check(scope.Resolve(source.Id) == root.Id && scope.Candidates.SequenceEqual(new[] { root.Id, other.Id }), "Initial viewport scope resolves every descendant to its root");
        Check(!scope.Enter(child.Id) && scope.ParentId is null, "Double-click cannot skip an editing level");
        Check(scope.Enter(root.Id) && scope.Resolve(source.Id) == child.Id, "Entering root exposes its immediate children");
        Check(scope.Resolve(other.Id) is null && !scope.Enter(other.Id), "An unrelated root is outside the active editing scope");
        Check(scope.Enter(child.Id) && scope.Candidates.SequenceEqual(new[] { source.Id }), "Nested editing exposes the individual source");
        Check(!scope.Enter(source.Id) && scope.ParentId == child.Id, "Double-clicking a source does not enter an invalid scope");
        Check(scope.Exit() == child.Id && scope.ParentId == root.Id, "Escape from nested editing returns exactly one level");
        Check(scope.Exit() == root.Id && scope.ParentId is null && scope.Exit() is null, "Escape returns to root selection and then does nothing");
        scope.Enter(root.Id); scope.Enter(child.Id);
        tree.Remove(child.Id); scope.Reset();
        Check(scope.Candidates.Contains(root.Id) && scope.Resolve(source.Id) == root.Id, "Structural edits can reset scope safely after deleting its active parent");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
