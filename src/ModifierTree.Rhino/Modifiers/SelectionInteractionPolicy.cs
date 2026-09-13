namespace ModifierTree.Rhino.Modifiers;

/// <summary>Pure guards that keep managed selection from restarting Rhino's native Gumball.</summary>
internal static class SelectionInteractionPolicy
{
    public static bool NeedsNativeReselection(Guid requestedNodeId, Guid? viewportSelectedNodeId, bool nativeSelectionMatches) =>
        viewportSelectedNodeId != requestedNodeId || !nativeSelectionMatches;

    public static bool PreserveNativeMouseDown(Guid pickedNodeId, Guid? viewportSelectedNodeId,
        bool nativeSelectionMatches, bool gumballVisible, bool displayedGeometryMatchesNative) =>
        displayedGeometryMatchesNative && gumballVisible && viewportSelectedNodeId == pickedNodeId && nativeSelectionMatches;

    public static bool PanelSelectionChanged(IReadOnlyList<Guid> previous, IReadOnlyList<Guid> current) =>
        !previous.SequenceEqual(current);

    public static bool CanRequestGumball(long selectionRevision, long attemptedRevision) =>
        selectionRevision != attemptedRevision;

    public static bool CanActivateGumball(long pendingRevision, long selectionRevision, long attemptedRevision) =>
        pendingRevision == selectionRevision && pendingRevision != attemptedRevision;
}
