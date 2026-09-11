using ModifierTree.Core;
using Rhino;
using Rhino.Commands;

namespace ModifierTree.Rhino.Persistence;

internal sealed class TreeStateChangedEventArgs(bool structureChanged, bool restored, bool geometryChanged = false) : EventArgs
{
    public bool StructureChanged { get; } = structureChanged;
    public bool Restored { get; } = restored;
    public bool GeometryChanged { get; } = structureChanged || geometryChanged;
}

/// <summary>Persistent private document data, with transactions on Rhino's existing Undo stack.</summary>
internal sealed class TreeDocumentData(RhinoDoc document) : IDisposable
{
    private bool _disposed;
    private bool _replaying;
    private RhinoDoc Document => document;
    public ModifierTreeModel Tree { get; } = new();
    public InputVisibility InputVisibility { get; } = new();
    public bool PreviewEnabled { get; private set; } = true;
    public string? ReadOnlyReason { get; set; }
    public event EventHandler<TreeStateChangedEventArgs>? Changed;

    public ModifierDocumentState Capture() => TreeStateCodec.Capture(Tree, InputVisibility, PreviewEnabled);

    public void Load(ModifierDocumentState state)
    {
        _ = TreeStateCodec.Encode(state); // Validate even when only settings differ.
        Apply(state, true);
    }

    public bool Edit(string description, Func<ModifierTreeModel, InputVisibility, bool> change,
        out string error, bool? previewEnabled = null)
    {
        error = "";
        if (_disposed || _replaying || document.UndoActive || document.RedoActive)
        { error = "Wait for the current document operation to finish."; return false; }
        if (ReadOnlyReason is { } reason) { error = reason; return false; }
        var before = Capture();
        var draft = new ModifierTreeModel();
        var visibility = new InputVisibility();
        TreeStateCodec.Apply(before, draft, visibility);
        if (!change(draft, visibility)) return false;
        ModifierDocumentState after;
        // Validation and no-op detection happen before opening a native Undo record.
        try
        {
            after = TreeStateCodec.Capture(draft, visibility, previewEnabled ?? PreviewEnabled);
            if (TreeStateCodec.Encode(before) == TreeStateCodec.Encode(after)) return false;
        }
        catch (FormatException exception) { error = exception.Message; return false; }
        uint record = 0;
        if (!document.UndoRecordingIsActive && document.UndoRecordingEnabled)
        {
            record = document.BeginUndoRecord(description);
            if (record == 0) { error = "Rhino could not start an Undo record. Try again after the current command finishes."; return false; }
        }
        try
        {
            if (document.UndoRecordingIsActive && !RecordInverse(description, before))
            { error = "Rhino could not record this tree edit for Undo."; return false; }
            Apply(after, false);
            document.Modified = true;
            return true;
        }
        finally { if (record != 0) document.EndUndoRecord(record); }
    }

    private bool RecordInverse(string description, ModifierDocumentState state) =>
        document.AddCustomUndoEvent(description, Replay, new UndoState(new WeakReference<TreeDocumentData>(this), state));

    private static void Replay(object? sender, CustomUndoEventArgs e)
    {
        if (e.Tag is not UndoState entry || !entry.Owner.TryGetTarget(out var owner) || owner._disposed ||
            e.Document.RuntimeSerialNumber != owner.Document.RuntimeSerialNumber) return;
        owner._replaying = true;
        try
        {
            // Register the opposite state first: Rhino uses this for Redo and subsequent Undo.
            if (!owner.RecordInverse(e.ActionDescription, owner.Capture()))
                RhinoApp.WriteLine("Modifier Tree: Rhino could not record the inverse tree state for Redo.");
            // Only private data changes here. Consumers defer native selection and drawing to Idle.
            owner.Apply(entry.State, true);
        }
        finally { owner._replaying = false; }
    }

    private void Apply(ModifierDocumentState state, bool restored)
    {
        var before = Capture();
        var structureChanged = !SameTree(before, state);
        var geometryChanged = !TreeStateCodec.SameGeometry(before, state);
        if (geometryChanged) TreeStateCodec.Apply(state, Tree, InputVisibility);
        else
        {
            // Names and display settings preserve node instances and Boolean caches.
            TreeStateCodec.ApplyMetadata(state, Tree, InputVisibility);
        }
        PreviewEnabled = state.PreviewEnabled;
        Changed?.Invoke(this, new TreeStateChangedEventArgs(structureChanged, restored, geometryChanged));
    }

    private static bool SameTree(ModifierDocumentState first, ModifierDocumentState second)
    {
        if (!first.Roots.SequenceEqual(second.Roots) || first.Nodes.Count != second.Nodes.Count) return false;
        var nodes = second.Nodes.ToDictionary(node => node.Id);
        return first.Nodes.All(node => nodes.TryGetValue(node.Id, out var other) && node.Kind == other.Kind &&
            node.ObjectId == other.ObjectId && node.ParentId == other.ParentId && node.Children.SequenceEqual(other.Children));
    }

    public void Dispose() { _disposed = true; Changed = null; }

    private sealed record UndoState(WeakReference<TreeDocumentData> Owner, ModifierDocumentState State);
}
