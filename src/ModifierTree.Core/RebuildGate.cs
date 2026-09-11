namespace ModifierTree.Core;

/// <summary>Coalesces object events and preserves pending work while Rhino is editing or undoing.</summary>
public sealed class RebuildGate
{
    public bool Pending { get; private set; }
    public void Request() => Pending = true;
    public void Clear() => Pending = false;

    public bool TryTake(bool commandRunning, bool undoActive, bool redoActive)
    {
        if (!Pending || commandRunning || undoActive || redoActive) return false;
        Pending = false;
        return true;
    }
}
