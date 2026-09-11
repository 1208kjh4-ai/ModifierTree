using Rhino;
using Rhino.DocObjects;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Owns only temporary object-mode changes; user-hidden objects are never claimed.</summary>
internal sealed class WorkingSourceVisibility(RhinoDoc document)
{
    private readonly Dictionary<Guid, ObjectMode> _originalModes = [];
    public bool IsBusy { get; private set; }

    public bool IsManagedHidden(Guid objectId) => _originalModes.ContainsKey(objectId) &&
        document.Objects.FindId(objectId)?.IsHidden == true;

    public bool IsLogicallyLocked(Guid objectId)
    {
        var obj = document.Objects.FindId(objectId);
        if (obj is null) return false;
        if (obj.IsLocked || _originalModes.GetValueOrDefault(objectId) == ObjectMode.Locked) return true;
        var layer = document.Layers.FindIndex(obj.Attributes.LayerIndex);
        var visited = new HashSet<Guid>();
        while (layer is not null && visited.Add(layer.Id))
        {
            if (layer.IsLocked) return true;
            layer = layer.ParentLayerId == Guid.Empty ? null : document.Layers.FindId(layer.ParentLayerId);
        }
        return false;
    }

    public void Apply(IEnumerable<Guid> hiddenIds)
    {
        if (IsBusy) throw new InvalidOperationException("Working-source visibility is already being updated.");
        var desired = hiddenIds.Where(id => id != Guid.Empty).ToHashSet();
        IsBusy = true;
        try
        {
            using var edit = new RuntimeDocumentEdit(document);
            foreach (var id in _originalModes.Keys.ToArray())
            {
                var obj = document.Objects.FindId(id);
                if (obj is null) { _originalModes.Remove(id); continue; }
                if (desired.Contains(id)) continue;
                var originalMode = _originalModes[id];
                if (obj.Attributes.Mode != originalMode)
                {
                    using var attributes = obj.Attributes.Duplicate();
                    attributes.Mode = originalMode;
                    if (!document.Objects.ModifyAttributes(id, attributes, true))
                        throw new InvalidOperationException("Rhino could not restore a working input's original visibility.");
                }
                _originalModes.Remove(id);
            }
            foreach (var id in desired)
            {
                var obj = document.Objects.FindId(id);
                if (obj is null || obj.IsHidden || !obj.Attributes.Visible) continue;
                var wasOwned = _originalModes.ContainsKey(id);
                if (!wasOwned) _originalModes.Add(id, obj.Attributes.Mode);
                using var attributes = obj.Attributes.Duplicate();
                attributes.Mode = ObjectMode.Hidden;
                try
                {
                    if (!document.Objects.ModifyAttributes(id, attributes, true))
                        throw new InvalidOperationException("Rhino could not hide a working input from object snaps.");
                }
                catch
                {
                    if (!wasOwned) _originalModes.Remove(id);
                    throw;
                }
            }
        }
        finally { IsBusy = false; }
    }

    public void RestoreAll() => Apply([]);
}
