using System.Text.Json;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;

namespace ModifierTree.Rhino.Diagnostics;

/// <summary>
/// Observes events only. Never edits document geometry from an event callback.
/// Store value snapshots rather than RhinoObject/EventArgs instances whose lifetime is limited.
/// </summary>
internal sealed class EventTrace : IDisposable
{
    private const int Capacity = 2000;
    private readonly RhinoDoc _document;
    private readonly object _gate = new();
    private readonly Queue<string> _records = new();
    private readonly HashSet<uint> _transforms = [];
    private long _sequence;
    private bool _enabled;
    private bool _disposed;

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value) _transforms.Clear();
            }
        }
    }

    public EventTrace(RhinoDoc document)
    {
        _document = document;
        RhinoDoc.BeforeTransformObjects += BeforeTransform;
        RhinoDoc.AfterTransformObjects += AfterTransform;
        RhinoDoc.ReplaceRhinoObject += ReplaceObject;
        RhinoDoc.AddRhinoObject += AddObject;
        RhinoDoc.DeleteRhinoObject += DeleteObject;
        RhinoDoc.UndeleteRhinoObject += UndeleteObject;
        RhinoDoc.ModifyObjectAttributes += AttributesChanged;
        Command.BeginCommand += BeginCommand;
        Command.EndCommand += EndCommand;
    }

    private bool IsOurDocument(RhinoDoc? document) =>
        document?.RuntimeSerialNumber == _document.RuntimeSerialNumber;

    private void Record(string name, object data)
    {
        lock (_gate)
        {
            if (!_enabled || _disposed) return;
            var line = JsonSerializer.Serialize(new
            {
                sequence = ++_sequence,
                utc = DateTimeOffset.UtcNow,
                document = _document.RuntimeSerialNumber,
                @event = name,
                undo = _document.UndoActive,
                redo = _document.RedoActive,
                data
            });
            if (_records.Count == Capacity) _records.Dequeue();
            _records.Enqueue(line);
        }
    }

    private void BeforeTransform(object? sender, RhinoTransformObjectsEventArgs e)
    {
        if (!Enabled) return;
        var objects = e.Objects;
        if (!objects.Any(obj => IsOurDocument(obj.Document))) return;
        lock (_gate)
        {
            // Bound unmatched events, including cancelled transforms, without assuming
            // that AfterTransform must arrive before EndCommand during this investigation.
            if (_transforms.Count >= Capacity) _transforms.Clear();
            _transforms.Add(e.TransformEventId);
        }
        var t = e.Transform;
        Record("BeforeTransform", new
        {
            transformId = e.TransformEventId,
            copy = e.ObjectsWillBeCopied,
            objects = objects.Select(obj => new { id = obj.Id, serial = obj.RuntimeSerialNumber }).ToArray(),
            gripCount = e.GripCount,
            matrix = new[]
            {
                t.M00, t.M01, t.M02, t.M03,
                t.M10, t.M11, t.M12, t.M13,
                t.M20, t.M21, t.M22, t.M23,
                t.M30, t.M31, t.M32, t.M33
            }
        });
    }

    private void AfterTransform(object? sender, RhinoAfterTransformObjectsEventArgs e)
    {
        lock (_gate)
        {
            if (!_transforms.Remove(e.TransformEventId)) return;
        }
        Record("AfterTransform", new { transformId = e.TransformEventId });
    }

    private void ReplaceObject(object? sender, RhinoReplaceObjectEventArgs e)
    {
        if (!Enabled || !IsOurDocument(e.OldRhinoObject?.Document)) return;
        Record("ReplaceObject", new
        {
            oldId = e.ObjectId,
            newId = e.NewRhinoObject?.Id,
            oldSerial = e.OldRhinoObject?.RuntimeSerialNumber,
            newSerial = e.NewRhinoObject?.RuntimeSerialNumber
        });
    }

    private void ObjectEvent(string name, RhinoObjectEventArgs e)
    {
        if (!Enabled || !IsOurDocument(e.TheObject?.Document)) return;
        Record(name, new { id = e.ObjectId, serial = e.TheObject?.RuntimeSerialNumber });
    }

    private void AddObject(object? sender, RhinoObjectEventArgs e) => ObjectEvent("AddObject", e);
    private void DeleteObject(object? sender, RhinoObjectEventArgs e) => ObjectEvent("DeleteObject", e);
    private void UndeleteObject(object? sender, RhinoObjectEventArgs e) => ObjectEvent("UndeleteObject", e);

    private void AttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        if (!Enabled || !IsOurDocument(e.Document)) return;
        Record("AttributesChanged", new { id = e.RhinoObject.Id, oldName = e.OldAttributes.Name, newName = e.NewAttributes.Name });
    }

    private void BeginCommand(object? sender, CommandEventArgs e)
    {
        if (Enabled && IsOurDocument(e.Document)) Record("BeginCommand", new { command = e.CommandEnglishName });
    }

    private void EndCommand(object? sender, CommandEventArgs e)
    {
        if (Enabled && IsOurDocument(e.Document))
            Record("EndCommand", new { command = e.CommandEnglishName, result = e.CommandResult.ToString() });
    }

    public string[] Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _transforms.Clear();
        }
        RhinoDoc.BeforeTransformObjects -= BeforeTransform;
        RhinoDoc.AfterTransformObjects -= AfterTransform;
        RhinoDoc.ReplaceRhinoObject -= ReplaceObject;
        RhinoDoc.AddRhinoObject -= AddObject;
        RhinoDoc.DeleteRhinoObject -= DeleteObject;
        RhinoDoc.UndeleteRhinoObject -= UndeleteObject;
        RhinoDoc.ModifyObjectAttributes -= AttributesChanged;
        Command.BeginCommand -= BeginCommand;
        Command.EndCommand -= EndCommand;
    }
}
