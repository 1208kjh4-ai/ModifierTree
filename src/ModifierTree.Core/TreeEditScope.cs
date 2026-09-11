namespace ModifierTree.Core;

/// <summary>Viewport picking addresses one level of the expression at a time.</summary>
public sealed class TreeEditScope(ModifierTreeModel tree)
{
    public Guid? ParentId { get; private set; }
    public IReadOnlyList<Guid> Candidates => tree.ChildrenOf(ParentId);

    public bool Enter(Guid nodeId)
    {
        if (!Candidates.Contains(nodeId) || tree.Find(nodeId)?.IsModifier != true) return false;
        ParentId = nodeId;
        return true;
    }

    public Guid? Exit()
    {
        var exited = ParentId;
        ParentId = exited is { } id ? tree.Find(id)?.ParentId : null;
        return exited;
    }

    public void Reset() => ParentId = null;

    public Guid? Resolve(Guid nodeId)
    {
        var node = tree.Find(nodeId);
        while (node is not null)
        {
            if (node.ParentId == ParentId) return node.Id;
            node = node.ParentId is { } parent ? tree.Find(parent) : null;
        }
        return null;
    }
}
