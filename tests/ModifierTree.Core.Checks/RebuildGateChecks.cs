using ModifierTree.Core;

internal static class RebuildGateChecks
{
    public static void Verify()
    {
        var gate = new RebuildGate();
        for (var i = 0; i < 3; i++) gate.Request(); // Replace/Delete/Add for one edit.
        Assert(!gate.TryTake(true, false, false) && gate.Pending, "Do not rebuild during a command");
        Assert(!gate.TryTake(false, true, false) && gate.Pending, "Undo must finish before rebuilding");
        Assert(!gate.TryTake(false, false, true) && gate.Pending, "Redo must finish before rebuilding");
        Assert(gate.TryTake(false, false, false) && !gate.TryTake(false, false, false),
            "Three related change events cause exactly one rebuild");
        Assert(!gate.TryTake(false, false, false), "A cancelled command without edits causes no rebuild");
        gate.Request();
        gate.Clear();
        Assert(!gate.TryTake(false, false, false), "Removing the operator drops pending work");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
