using ModifierTree.Core;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Owns the current display cage for each control; returned cages are borrowed until its next change.</summary>
internal sealed class BendControlCageCache : IDisposable
{
    private const string CornerKey = "ModifierTree.ControlBox.Corners";
    private readonly Dictionary<Guid, Entry> _entries = [];
    private bool _disposed;

    public int Count => _entries.Count;

    public BendControlCage? Get(Guid nodeId, GeometryBase geometry, ControlBoxSettings settings,
        double tolerance, Transform? transform = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // DataCRC does not promise to include user strings. Corner identities determine the
        // box's local axes, so changing or removing them must invalidate the cage as well.
        var key = new Key(geometry.GetType(), geometry.DataCRC(0), geometry.GetUserString(CornerKey),
            settings, tolerance, transform);
        if (_entries.TryGetValue(nodeId, out var previous) && previous.Key == key)
            return previous.Cage;

        // Keep one state per node, including invalid geometry. A damaged box must never
        // leave yesterday's valid cage visible or pickable. Drag transforms are absolute;
        // cancellation (null transform) is a new request independent of live-evaluation timing.
        if (_entries.Remove(nodeId, out previous)) previous.Cage?.Dispose();
        var cage = BendControlCage.Create(geometry, settings, tolerance, transform);
        _entries.Add(nodeId, new Entry(key, cage));
        return cage;
    }

    public void Retain(IEnumerable<Guid> nodeIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var retained = nodeIds.ToHashSet();
        foreach (var id in _entries.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            _entries[id].Cage?.Dispose();
            _entries.Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values) entry.Cage?.Dispose();
        _entries.Clear();
    }

    public void Dispose()
    {
        Clear();
        _disposed = true;
    }

    private readonly record struct Key(Type GeometryType, uint GeometryCrc, string? Corners,
        ControlBoxSettings Settings, double Tolerance, Transform? Transform);
    private sealed record Entry(Key Key, BendControlCage? Cage);
}
