using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Reconciles successful native result transformations while the command's Undo record is still open.</summary>
internal sealed class WorkingResultTransformEvents : IDisposable
{
    private readonly RhinoDoc _document;
    private readonly TreeDocumentData _data;
    private readonly WorkingResultObjects _working;
    private readonly Action _changed;
    private readonly Dictionary<uint, Pending> _pending = [];
    public WorkingResultTransformEvents(RhinoDoc document, TreeDocumentData data, WorkingResultObjects working, Action changed)
    {
        _document = document; _data = data; _working = working; _changed = changed;
        RhinoDoc.BeforeTransformObjects += OnBefore;
        RhinoDoc.AfterTransformObjects += OnAfter;
    }
    private void OnBefore(object? sender, RhinoTransformObjectsEventArgs e)
    {
        if (_working.IsBusy || _document.UndoActive || _document.RedoActive || e.ObjectsWillBeCopied || e.GripCount != 0) return;
        var objects = e.Objects.Where(obj => obj.Document?.RuntimeSerialNumber == _document.RuntimeSerialNumber).ToArray();
        var proxies = objects.Where(obj => _working.IsProxy(obj.Id)).ToDictionary(obj => obj.Id, obj => obj.RuntimeSerialNumber);
        if (proxies.Count == 0) return;
        if (_pending.Count > 128) _pending.Clear();
        var sources = objects.Where(obj => _data.Tree.FindSource(obj.Id) is not null).Select(obj => obj.Id).ToHashSet();
        _pending[e.TransformEventId] = new(e.Transform, proxies, sources, _document.CurrentUndoRecordSerialNumber);
    }
    private void OnAfter(object? sender, RhinoAfterTransformObjectsEventArgs e)
    {
        if (!_pending.Remove(e.TransformEventId, out var pending) || _document.UndoActive || _document.RedoActive) return;
        var nodes = pending.Proxies.Where(pair => _document.Objects.FindId(pair.Key) is { } obj && obj.RuntimeSerialNumber != pair.Value)
            .Select(pair => _working.NodeForObject(pair.Key)).Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        if (nodes.Length == 0) return;
        try
        {
            if (_document.UndoRecordingEnabled && (!_document.UndoRecordingIsActive || pending.UndoRecord != _document.CurrentUndoRecordSerialNumber))
                throw new InvalidOperationException("The result transform could not join Rhino's Undo record. Use Undo and try again.");
            if (!WorkingResultTransform.Apply(_document, _data, nodes, pending.Transform,
                _working.Sources.IsManagedHidden, _working.Sources.IsLogicallyLocked, out var error, pending.Sources))
                RhinoApp.WriteLine("Modifier Tree: " + error);
        }
        catch (Exception exception) { RhinoApp.WriteLine("Modifier Tree: " + exception.Message); }
        finally { _working.Invalidate(); _changed(); }
    }
    public void Clear() => _pending.Clear();
    public void Dispose()
    {
        RhinoDoc.BeforeTransformObjects -= OnBefore;
        RhinoDoc.AfterTransformObjects -= OnAfter;
        Clear();
    }
    private sealed record Pending(Transform Transform, Dictionary<Guid, uint> Proxies, HashSet<Guid> Sources, uint UndoRecord);
}
