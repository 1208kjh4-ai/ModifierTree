namespace ModifierTree.Core;

/// <summary>Explicit wire visibility is independent of tree/viewport selection.</summary>
public sealed class InputVisibility
{
    private HashSet<Guid> _visible = [];
    public bool IsVisible(Guid objectId) => _visible.Contains(objectId);
    public void Set(Guid objectId, bool visible)
    {
        if (visible) _visible.Add(objectId);
        else _visible.Remove(objectId);
    }

    internal void RestoreValidated(HashSet<Guid> visible) => _visible = visible;
}
