using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Joins frame settings to the native transform's existing Undo record; never edits native objects in callbacks.</summary>
internal sealed class PatternTransformTracker : IDisposable
{
    private readonly RhinoDoc _document;
    private readonly TreeDocumentData _data;
    private readonly Func<Guid?> _selectedNode;
    private readonly Action<string> _report;
    private readonly Func<Guid, bool>? _isWorkingResult;
    private readonly Dictionary<uint, PendingTransform> _pending = [];

    public PatternTransformTracker(RhinoDoc document, TreeDocumentData data, Func<Guid?> selectedNode, Action<string>? report = null,
        Func<Guid, bool>? isWorkingResult = null)
    {
        _document = document;
        _data = data;
        _selectedNode = selectedNode;
        _report = report ?? (message => RhinoApp.WriteLine(message));
        _isWorkingResult = isWorkingResult;
        RhinoDoc.BeforeTransformObjects += OnBefore;
        RhinoDoc.AfterTransformObjects += OnAfter;
    }

    private void OnBefore(object? sender, RhinoTransformObjectsEventArgs e)
    {
        if (e.GripCount != 0 || _isWorkingResult is not null && e.Objects.Any(obj => _isWorkingResult(obj.Id))) return;
        var ids = e.Objects.Where(obj => obj.Document?.RuntimeSerialNumber == _document.RuntimeSerialNumber).Select(obj => obj.Id).ToArray();
        if (!Begin(e.TransformEventId, ids, e.Transform, e.ObjectsWillBeCopied, _selectedNode(), out var error) && error.Length > 0)
            _report(error);
    }

    private void OnAfter(object? sender, RhinoAfterTransformObjectsEventArgs e)
    {
        if (!Complete(e.TransformEventId, out var error) && error.Length > 0) _report(error);
    }

    // Value-only entry points allow native geometry/Undo checks without fabricating SDK event args.
    internal bool Begin(uint eventId, IEnumerable<Guid> objectIds, Transform transform, bool copy, Guid? selectedNode, out string error)
    {
        error = "";
        _pending.Remove(eventId);
        if (copy || _document.UndoActive || _document.RedoActive || _data.ReadOnlyReason is not null ||
            selectedNode is not { } selected || _data.Tree.Find(selected) is not { IsModifier: true }) return true;
        var sources = _data.Tree.SourcesInSubtree(selected);
        var transformedIds = objectIds.ToHashSet();
        if (sources.Count == 0 || !sources.All(transformedIds.Contains)) return true;
        var changes = new Dictionary<Guid, ArraySettings>();
        foreach (var array in PatternFrameTransform.ArraysInSubtree(_data.Tree, selected))
        {
            if (!PatternFrameTransform.TryTransform(array.Array!, transform, out var settings, out error)) return false;
            if (settings != array.Array) changes.Add(array.Id, settings);
        }
        if (changes.Count == 0) return true;
        var serials = new Dictionary<Guid, uint>();
        foreach (var id in sources)
        {
            if (_document.Objects.FindId(id) is not { } obj) return true;
            serials.Add(id, obj.RuntimeSerialNumber);
        }
        if (_pending.Count > 128) _pending.Clear();
        _pending[eventId] = new PendingTransform(changes, serials, _document.CurrentUndoRecordSerialNumber);
        return true;
    }

    internal bool Complete(uint eventId, out string error)
    {
        error = "";
        if (!_pending.Remove(eventId, out var pending)) return true;
        if (_document.UndoActive || _document.RedoActive) return true;
        // Cancellation or a partial native transform cannot rotate the whole arrangement's frame.
        if (pending.Serials.Any(pair => _document.Objects.FindId(pair.Key) is not { } obj || obj.RuntimeSerialNumber == pair.Value)) return true;
        if (_document.UndoRecordingEnabled && (!_document.UndoRecordingIsActive ||
            pending.UndoRecord != _document.CurrentUndoRecordSerialNumber))
        { error = "Array axes could not join Rhino's transform Undo record. Undo the transform and try again."; return false; }
        var validationError = "";
        var before = _data.Capture();
        try
        {
            var changed = _data.Edit("Transform Array axes", (tree, _) =>
            {
                foreach (var pair in pending.Settings)
                    if (!tree.SetArraySettings(pair.Key, pair.Value, out validationError)) return false;
                return true;
            }, out error);
            if (validationError.Length > 0) error = validationError;
            return changed || error.Length == 0;
        }
        catch (Exception exception)
        {
            try { _data.Load(before); } catch { /* The native Undo record still owns the inverse snapshot. */ }
            error = "Array axis update failed: " + exception.Message + " Use Undo to restore the transform.";
            return false;
        }
    }

    public void Clear() => _pending.Clear();
    public void Dispose()
    {
        RhinoDoc.BeforeTransformObjects -= OnBefore;
        RhinoDoc.AfterTransformObjects -= OnAfter;
        Clear();
    }

    private sealed record PendingTransform(Dictionary<Guid, ArraySettings> Settings, Dictionary<Guid, uint> Serials, uint UndoRecord);
}
