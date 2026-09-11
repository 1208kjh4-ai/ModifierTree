namespace ModifierTree.Core;

/// <summary>A detached, immutable document snapshot. Source GUIDs may refer to missing Rhino objects.</summary>
public sealed class ModifierDocumentState
{
    public ModifierDocumentState(int schemaVersion, IEnumerable<Guid> roots, IEnumerable<ModifierNodeState> nodes,
        bool previewEnabled, IEnumerable<Guid> visibleInputIds)
    {
        SchemaVersion = schemaVersion;
        Roots = Array.AsReadOnly(roots.ToArray());
        Nodes = Array.AsReadOnly(nodes.ToArray());
        PreviewEnabled = previewEnabled;
        VisibleInputIds = Array.AsReadOnly(visibleInputIds.ToArray());
    }

    public int SchemaVersion { get; }
    public IReadOnlyList<Guid> Roots { get; }
    public IReadOnlyList<ModifierNodeState> Nodes { get; }
    public bool PreviewEnabled { get; }
    public IReadOnlyList<Guid> VisibleInputIds { get; }
}

public sealed class ModifierNodeState
{
    public ModifierNodeState(Guid id, TreeNodeKind kind, Guid? objectId, Guid? parentId, IEnumerable<Guid> children, string name = "",
        bool enabled = true, MirrorSettings? mirror = null, ArraySettings? array = null, ControlBoxSettings? controlBox = null)
    {
        Id = id;
        Kind = kind;
        ObjectId = objectId;
        ParentId = parentId;
        Children = System.Array.AsReadOnly(children.ToArray());
        Name = name;
        Enabled = enabled;
        Mirror = mirror ?? (kind == TreeNodeKind.Mirror ? MirrorSettings.Default : null);
        Array = array ?? (kind == TreeNodeKind.Array ? ArraySettings.Default : null);
        ControlBox = controlBox ?? (kind == TreeNodeKind.ControlBox ? ControlBoxSettings.Default : null);
    }

    public Guid Id { get; }
    public TreeNodeKind Kind { get; }
    public Guid? ObjectId { get; }
    public Guid? ParentId { get; }
    public IReadOnlyList<Guid> Children { get; }
    public string Name { get; }
    public bool Enabled { get; }
    public MirrorSettings? Mirror { get; }
    public ArraySettings? Array { get; }
    public ControlBoxSettings? ControlBox { get; }
}
