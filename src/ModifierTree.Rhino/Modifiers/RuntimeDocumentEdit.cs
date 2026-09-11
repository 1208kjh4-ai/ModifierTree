using Rhino;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Derived viewport state must not create native history or dirty the saved model.</summary>
internal sealed class RuntimeDocumentEdit : IDisposable
{
    private readonly RhinoDoc _document;
    private readonly bool _undoEnabled;
    private readonly bool _modified;
    private bool _disposed;

    public RuntimeDocumentEdit(RhinoDoc document)
    {
        if (document.UndoActive || document.RedoActive)
            throw new InvalidOperationException("Wait for Undo or Redo to finish before updating the working preview.");
        _document = document;
        _undoEnabled = document.UndoRecordingEnabled;
        _modified = document.Modified;
        document.UndoRecordingEnabled = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _document.UndoRecordingEnabled = _undoEnabled; }
        finally { _document.Modified = _modified; }
    }
}
