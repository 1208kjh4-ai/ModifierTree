using ModifierTree.Core;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Native, derived result objects supply Rhino picking and object snaps. They never belong to the saved tree.</summary>
internal sealed class WorkingResultObjects : IDisposable
{
    internal const string Marker = "ModifierTree.WorkingResult";
    private readonly RhinoDoc _document;
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<Guid, Guid> _owners = [];
    private readonly Dictionary<uint, Guid> _serialOwners = [];
    private readonly Func<Brep, ObjectAttributes, Guid> _addResult;
    private readonly HashSet<Guid> _restoreSelection = [];
    private bool _busy;
    public WorkingSourceVisibility Sources { get; }
    public bool IsBusy => _busy || Sources.IsBusy;
    public IEnumerable<Guid> ObjectIds
    {
        get
        {
            DiscoverRestoredObjects();
            return _owners.Keys.Where(id => _document.Objects.FindId(id) is not null).ToArray();
        }
    }
    public WorkingResultObjects(RhinoDoc document, Func<Brep, ObjectAttributes, Guid>? addResult = null)
    {
        _document = document; Sources = new(document);
        _addResult = addResult ?? ((brep, attributes) => document.Objects.AddBrep(brep, attributes));
    }
    public Guid? ObjectForNode(Guid nodeId) => _entries.TryGetValue(nodeId, out var entry) &&
        _document.Objects.FindId(entry.ObjectId) is not null ? entry.ObjectId : null;
    public Guid? NodeForObject(Guid objectId)
    {
        var obj = _document.Objects.FindId(objectId);
        if (!_owners.TryGetValue(objectId, out var nodeId) &&
            (obj is null || !_serialOwners.TryGetValue(obj.RuntimeSerialNumber, out nodeId))) return null;
        if (obj is not null) RememberObject(nodeId, obj);
        return nodeId;
    }
    public bool IsProxy(Guid objectId) => NodeForObject(objectId).HasValue;
    public bool ObserveReplacement(Guid objectId, RhinoObject? previous, RhinoObject? replacement)
    {
        var nodeId = NodeForObject(objectId);
        if (nodeId is null && previous is not null && _serialOwners.TryGetValue(previous.RuntimeSerialNumber, out var known)) nodeId = known;
        if (nodeId is not { } owner) return false;
        // The new object may not have joined the document or received its GUID yet.
        // Keep both native versions so a later Undo/Redo can identify either one.
        if (previous is not null) _serialOwners[previous.RuntimeSerialNumber] = owner;
        if (replacement is not null) _serialOwners[replacement.RuntimeSerialNumber] = owner;
        return true;
    }
    public bool HasObject(Guid nodeId) => ObjectForNode(nodeId).HasValue;
    public bool IsVisible(Guid nodeId) => !_entries.TryGetValue(nodeId, out var entry) ||
        (_document.Objects.FindId(entry.ObjectId) is { } obj ? !obj.IsHidden && obj.Visible : entry.Mode != ObjectMode.Hidden);
    public void ClearPendingSelection() => _restoreSelection.Clear();
    public void Invalidate() { foreach (var entry in _entries.Values) entry.Generation = -1; }

    public void Synchronize(ModifierTreeModel tree, TreeEvaluator evaluator, Guid? scope, bool enabled)
    {
        if (IsBusy) return;
        _busy = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(_document);
            ReconcileIdentities();
            var desired = new HashSet<Guid>();
            if (enabled)
            {
                foreach (var id in tree.Roots.Concat(scope is { } parent ? tree.ChildrenOf(parent) : []))
                    if (tree.Find(id)?.IsModifier == true && evaluator.Find(id) is { IsCurrent: true, HasResult: true, Results.Count: > 0 }) desired.Add(id);
            }
            // Establish valid result objects before hiding sources. A failed update leaves originals accessible.
            foreach (var id in desired)
            {
                var node = tree.Find(id)!;
                if (!_entries.TryGetValue(id, out var entry)) _entries.Add(id, entry = new Entry(Guid.NewGuid()));
                var existing = _document.Objects.FindId(entry.ObjectId);
                if (existing is not null) RememberObject(id, existing);
                var sourceId = tree.GeometrySourcesInSubtree(id).FirstOrDefault();
                var source = _document.Objects.FindId(sourceId);
                using var attributes = source?.Attributes.Duplicate() ?? new ObjectAttributes { LayerIndex = _document.Layers.CurrentLayerIndex };
                attributes.ObjectId = entry.ObjectId;
                attributes.Name = ModifierNames.ResultName(node);
                attributes.Mode = ObjectMode.Normal;
                attributes.Visible = true;
                attributes.RemoveFromAllGroups();
                attributes.SetUserString(Marker, id.ToString("D"));
                var appearance = AppearanceKey(attributes);
                if (existing is not null && entry.Generation == evaluator.RebuildCount && entry.Appearance == appearance && entry.Name == node.Name) continue;
                using var brep = ResultGeometry.Create(evaluator.Find(id)!);
                if (existing is not null) entry.Mode = existing.Attributes.Mode;
                attributes.Mode = entry.Mode;
                var wasSelected = existing?.IsSelected(false) > 0 || _restoreSelection.Contains(id);
                if (existing is null)
                {
                    // Rhino may reserve an old GUID in native history and assign a new
                    // one. The return value is authoritative: never leave that object unowned.
                    var actualId = _addResult(brep, attributes);
                    if (actualId == Guid.Empty)
                        throw new InvalidOperationException("Rhino could not create the working result.");
                    entry.ObjectId = actualId;
                    entry.KnownIds.Add(actualId);
                    _owners[actualId] = id;
                    attributes.ObjectId = actualId;
                    appearance = AppearanceKey(attributes);
                }
                else if (!_document.Objects.Replace(entry.ObjectId, (GeometryBase)brep, true) || !_document.Objects.ModifyAttributes(entry.ObjectId, attributes, true))
                    throw new InvalidOperationException("Rhino could not update the working result.");
                if (_document.Objects.FindId(entry.ObjectId) is { } updated) RememberObject(id, updated);
                if (wasSelected) _document.Objects.Select(entry.ObjectId, true, true, true);
                _restoreSelection.Remove(id);
                entry.Generation = evaluator.RebuildCount;
                entry.Appearance = appearance;
                entry.Name = node.Name;
            }
            foreach (var pair in _entries.Where(pair => !desired.Contains(pair.Key)).ToArray()) Remove(pair.Key, pair.Value);
            // InputWire is deliberately absent: drawing a wire does not enable snapping to it.
            var hidden = enabled ? tree.Nodes.Where(node => node.ObjectId.HasValue && node.ParentId.HasValue && node.ParentId != scope)
                .Select(node => node.ObjectId!.Value).ToArray() : [];
            Sources.Apply(hidden);
            // Keep identity tombstones for removed nodes: native Undo can resurrect
            // an earlier result object and must not create a second snap target.
        }
        catch
        {
            Sources.RestoreAll();
            throw;
        }
        finally { _busy = false; }
    }

    private void ReconcileIdentities()
    {
        DiscoverRestoredObjects();
        foreach (var (nodeId, entry) in _entries)
        {
            var alive = entry.KnownIds.Where(id => _document.Objects.FindId(id) is not null).ToArray();
            if (alive.Length == 0) continue;
            if (!alive.Contains(entry.ObjectId))
            {
                entry.ObjectId = alive[0];
                entry.Generation = -1;
            }
            foreach (var duplicate in alive.Where(id => id != entry.ObjectId))
            {
                RemoveObject(nodeId, duplicate);
                entry.Generation = -1;
            }
        }
    }

    private static string AppearanceKey(ObjectAttributes attributes)
    {
        using var copy = attributes.Duplicate();
        copy.Mode = ObjectMode.Normal;
        copy.Visible = true;
        return copy.ToJSON(new global::Rhino.FileIO.SerializationOptions());
    }

    private void RememberObject(Guid nodeId, RhinoObject obj)
    {
        _owners[obj.Id] = nodeId;
        _serialOwners[obj.RuntimeSerialNumber] = nodeId;
        _entries[nodeId].KnownIds.Add(obj.Id);
    }

    private void DiscoverRestoredObjects()
    {
        // Native Undo can resurrect an older runtime object under a NEW GUID when
        // the current derived cache occupies its old GUID. Its runtime serial persists.
        // Copies have different serials and must remain independent ordinary objects.
        foreach (var (serial, nodeId) in _serialOwners)
        {
            var obj = RhinoObject.FromRuntimeSerialNumber(serial);
            if (obj is null || obj.IsDeleted || obj.Document?.RuntimeSerialNumber != _document.RuntimeSerialNumber) continue;
            _owners[obj.Id] = nodeId;
            _entries[nodeId].KnownIds.Add(obj.Id);
        }
    }

    private void Remove(Guid nodeId, Entry entry)
    {
        var obj = _document.Objects.FindId(entry.ObjectId);
        if (obj is not null) entry.Mode = obj.Attributes.Mode;
        foreach (var id in entry.KnownIds) RemoveObject(nodeId, id);
        entry.Generation = -1;
    }

    private void RemoveObject(Guid nodeId, Guid objectId)
    {
        var obj = _document.Objects.FindId(objectId);
        if (obj is null) return;
        if (obj.IsSelected(false) > 0) _restoreSelection.Add(nodeId);
        if (!_document.Objects.Delete(obj, true, true)) throw new InvalidOperationException("Rhino could not remove a working result.");
    }

    /// <summary>Used before saving and authoring operations so native Undo/save sees original object modes.</summary>
    public void Suspend()
    {
        if (IsBusy) return;
        _busy = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(_document);
            DiscoverRestoredObjects();
            Sources.RestoreAll();
            foreach (var pair in _entries) Remove(pair.Key, pair.Value);
        }
        finally { _busy = false; }
    }

    /// <summary>Rhino Copy produces an independent fixed Brep, not a second link to the source tree.</summary>
    public void DetachCopiedMarker(Guid objectId)
    {
        if (IsBusy || IsProxy(objectId) || _document.UndoActive || _document.RedoActive ||
            _document.Objects.FindId(objectId) is not { } obj || obj.Attributes.GetUserString(Marker) is null) return;
        _busy = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(_document);
            using var attributes = obj.Attributes.Duplicate();
            attributes.SetUserString(Marker, null);
            _document.Objects.ModifyAttributes(objectId, attributes, true);
        }
        finally { _busy = false; }
    }

    public void SetExportMarkers(bool restore)
    {
        _busy = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(_document);
            ReconcileIdentities();
            foreach (var (objectId, nodeId) in _owners)
            {
                if (_document.Objects.FindId(objectId) is not { } obj) continue;
                using var attributes = obj.Attributes.Duplicate();
                attributes.SetUserString(Marker, restore ? nodeId.ToString("D") : null);
                if (!_document.Objects.ModifyAttributes(objectId, attributes, true))
                    throw new InvalidOperationException("Rhino could not prepare result attributes for export.");
            }
        }
        finally { _busy = false; }
    }

    public void Dispose() => Suspend();
    public void AssertReadyForSave(ModifierTreeModel tree)
    {
        if (ObjectIds.Any() || tree.Nodes.Any(node => node.ObjectId is { } id && Sources.IsManagedHidden(id)))
            throw new InvalidOperationException("The working display has not finished preparing for save. The save was stopped to protect the original objects; try saving again.");
    }
    private sealed class Entry(Guid objectId)
    {
        public Guid ObjectId { get; set; } = objectId;
        public HashSet<Guid> KnownIds { get; } = [];
        public int Generation { get; set; } = -1;
        public string Appearance { get; set; } = "";
        public string Name { get; set; } = "";
        public ObjectMode Mode { get; set; } = ObjectMode.Normal;
    }
}
