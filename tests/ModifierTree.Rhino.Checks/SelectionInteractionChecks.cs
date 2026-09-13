using ModifierTree.Rhino.Modifiers;

internal static class SelectionInteractionChecks
{
    public static void Run()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Check(!SelectionInteractionPolicy.NeedsNativeReselection(first, first, true),
            "Repeated managed selection preserves the active native selection and Gumball");
        Check(SelectionInteractionPolicy.NeedsNativeReselection(first, first, false),
            "A lost, replaced, or non-persistent native selection is restored even when the node ID is unchanged");
        Check(SelectionInteractionPolicy.NeedsNativeReselection(second, first, true),
            "Selecting a different tree node replaces the native selection");

        Check(SelectionInteractionPolicy.PreserveNativeMouseDown(first, first, true, true, true),
            "A current selected-object mouse-down passes through to a visible native Gumball");
        Check(!SelectionInteractionPolicy.PreserveNativeMouseDown(second, first, true, true, true),
            "A different tree item still uses managed viewport picking");
        Check(!SelectionInteractionPolicy.PreserveNativeMouseDown(first, first, false, true, true),
            "A stale node selection cannot bypass native selection repair");
        Check(!SelectionInteractionPolicy.PreserveNativeMouseDown(first, first, true, false, true),
            "A normal click remains managed when no native Gumball is visible");
        Check(!SelectionInteractionPolicy.PreserveNativeMouseDown(first, first, true, true, false),
            "A curved Control Box cage keeps managed picking when its display differs from the native box");

        Check(!SelectionInteractionPolicy.PanelSelectionChanged([first], [first]),
            "Programmatic restoration of the same panel row does not reselect Rhino objects");
        Check(SelectionInteractionPolicy.PanelSelectionChanged([first], [second]),
            "A genuinely different panel row still selects its Rhino object");

        Check(SelectionInteractionPolicy.CanRequestGumball(7, 6) &&
              SelectionInteractionPolicy.CanActivateGumball(7, 7, 6),
            "A new selection revision can queue and activate its Gumball once");
        Check(!SelectionInteractionPolicy.CanRequestGumball(7, 7) &&
              !SelectionInteractionPolicy.CanActivateGumball(7, 7, 7),
            "An attempted selection revision cannot queue another asynchronous Gumball command");
        Check(!SelectionInteractionPolicy.CanActivateGumball(7, 8, 6),
            "A stale Gumball request cannot activate after the native selection changes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
