using System.Text;
using System.Text.Json;

namespace ModifierTree.Core;

/// <summary>Versioned, bounded storage shared by document persistence and tree Undo/Redo.</summary>
public static class TreeStateCodec
{
    public const int CurrentSchemaVersion = 5;
    public const int MaxNodeCount = 10_000;
    public const int MaxTreeDepth = 128;
    public const int MaxJsonLength = 8 * 1024 * 1024;

    public static ModifierDocumentState Capture(ModifierTreeModel tree, InputVisibility visibility, bool previewEnabled)
    {
        var nodes = tree.Nodes.OrderBy(node => node.Id).ToArray();
        var state = new ModifierDocumentState(CurrentSchemaVersion, tree.Roots,
            nodes.Select(node => new ModifierNodeState(node.Id, node.Kind, node.ObjectId, node.ParentId, node.Children, node.Name,
                node.Enabled, node.Mirror, node.Array, node.ControlBox)),
            previewEnabled,
            nodes.Where(node => node.ObjectId is { } id && visibility.IsVisible(id)).Select(node => node.ObjectId!.Value));
        Validate(state);
        return state;
    }

    public static string Encode(ModifierDocumentState state)
    {
        Validate(state);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", state.SchemaVersion);
            WriteIds(writer, "roots", state.Roots);
            writer.WriteStartArray("nodes");
            foreach (var node in state.Nodes.OrderBy(node => node.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("id", node.Id);
                writer.WriteString("kind", node.Kind.ToString());
                writer.WriteString("name", node.Name);
                writer.WriteBoolean("enabled", node.Enabled);
                WriteMirror(writer, node.Mirror);
                WriteArray(writer, node.Array);
                WriteControlBox(writer, node.ControlBox);
                if (node.ObjectId is { } objectId) writer.WriteString("objectId", objectId);
                else writer.WriteNull("objectId");
                if (node.ParentId is { } parentId) writer.WriteString("parentId", parentId);
                else writer.WriteNull("parentId");
                WriteIds(writer, "children", node.Children);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteBoolean("previewEnabled", state.PreviewEnabled);
            WriteIds(writer, "visibleInputIds", state.VisibleInputIds.OrderBy(id => id));
            writer.WriteEndObject();
        }
        if (stream.Length > MaxJsonLength) throw Invalid("The saved tree is too large.");
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public static ModifierDocumentState Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Invalid("The saved tree is empty.");
        if (json.Length > MaxJsonLength || Encoding.UTF8.GetByteCount(json) > MaxJsonLength)
            throw Invalid("The saved tree is too large.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Fields(root, "schemaVersion", "roots", "nodes", "previewEnabled", "visibleInputIds");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var version)) throw Invalid("Invalid schema version.");
            if (version is not (1 or 2 or 3 or 4) && version != CurrentSchemaVersion) throw Invalid($"Unsupported tree schema version: {version}.");
            var array = root.GetProperty("nodes");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaxNodeCount)
                throw Invalid("Invalid or oversized node list.");
            var nodes = new List<ModifierNodeState>(array.GetArrayLength());
            foreach (var node in array.EnumerateArray())
            {
                if (version == 1) Fields(node, "id", "kind", "objectId", "parentId", "children");
                else if (version == 2) Fields(node, "id", "kind", "objectId", "parentId", "children", "name");
                else if (version < 5) Fields(node, "id", "kind", "objectId", "parentId", "children", "name", "enabled", "mirror", "array");
                else Fields(node, "id", "kind", "objectId", "parentId", "children", "name", "enabled", "mirror", "array", "controlBox");
                var kind = node.GetProperty("kind").GetString() switch
                {
                    nameof(TreeNodeKind.Geometry) => TreeNodeKind.Geometry,
                    nameof(TreeNodeKind.BooleanDifference) => TreeNodeKind.BooleanDifference,
                    nameof(TreeNodeKind.BooleanUnion) => TreeNodeKind.BooleanUnion,
                    nameof(TreeNodeKind.BooleanIntersection) => TreeNodeKind.BooleanIntersection,
                    nameof(TreeNodeKind.Mirror) when version >= 3 => TreeNodeKind.Mirror,
                    nameof(TreeNodeKind.Array) when version >= 3 => TreeNodeKind.Array,
                    nameof(TreeNodeKind.BasePlane) when version >= 4 => TreeNodeKind.BasePlane,
                    nameof(TreeNodeKind.Bend) when version >= 5 => TreeNodeKind.Bend,
                    nameof(TreeNodeKind.ControlBox) when version >= 5 => TreeNodeKind.ControlBox,
                    _ => throw Invalid("Unsupported tree node kind.")
                };
                var enabled = version < 3 || node.GetProperty("enabled").GetBoolean();
                var mirror = version < 3 ? null : ReadMirror(node.GetProperty("mirror"), version);
                var arraySettings = version < 3 ? null : ReadArray(node.GetProperty("array"), version);
                var controlBox = version < 5 ? null : ReadControlBox(node.GetProperty("controlBox"));
                if (!ModifierSettings.TryValidate(kind, enabled, mirror, arraySettings, controlBox, out var settingsError)) throw Invalid(settingsError);
                nodes.Add(new ModifierNodeState(ReadId(node.GetProperty("id")), kind,
                    ReadOptionalId(node.GetProperty("objectId")), ReadOptionalId(node.GetProperty("parentId")),
                    ReadIds(node.GetProperty("children")), version == 1 ? "" :
                        node.GetProperty("name").GetString() ?? throw Invalid("Invalid Modifier name."), enabled, mirror, arraySettings, controlBox));
            }
            var state = new ModifierDocumentState(CurrentSchemaVersion, ReadIds(root.GetProperty("roots")), nodes,
                root.GetProperty("previewEnabled").GetBoolean(), ReadIds(root.GetProperty("visibleInputIds")));
            Validate(state);
            return state;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FormatException("Invalid Modifier Tree document data.", ex);
        }
    }

    /// <summary>Validates and prepares replacements before mutating either existing model.</summary>
    public static void Apply(ModifierDocumentState state, ModifierTreeModel tree, InputVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(visibility);
        Validate(state);
        var visible = new HashSet<Guid>(state.VisibleInputIds);
        tree.RestoreValidated(state);
        visibility.RestoreValidated(visible);
    }

    /// <summary>Updates presentation data for an identical tree without invalidating geometry caches or node references.</summary>
    public static void ApplyMetadata(ModifierDocumentState state, ModifierTreeModel tree, InputVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(visibility);
        Validate(state);
        if (!state.Roots.SequenceEqual(tree.Roots) || state.Nodes.Count != tree.SourceCount + tree.ModifierCount)
            throw Invalid("Metadata can only be applied to the same tree structure.");
        foreach (var saved in state.Nodes)
        {
            var node = tree.Find(saved.Id);
            if (node is null || node.Kind != saved.Kind || node.ObjectId != saved.ObjectId || node.ParentId != saved.ParentId ||
                !node.Children.SequenceEqual(saved.Children) || node.Enabled != saved.Enabled || node.Mirror != saved.Mirror || node.Array != saved.Array ||
                node.ControlBox != saved.ControlBox)
                throw Invalid("Metadata can only be applied when tree structure and geometry settings are identical.");
        }
        var visible = new HashSet<Guid>(state.VisibleInputIds);
        tree.RestoreMetadataValidated(state);
        visibility.RestoreValidated(visible);
    }

    /// <summary>Compares geometry-affecting fields in validated snapshots, ignoring names and display preferences.</summary>
    public static bool SameGeometry(ModifierDocumentState first, ModifierDocumentState second)
    {
        if (!first.Roots.SequenceEqual(second.Roots) || first.Nodes.Count != second.Nodes.Count) return false;
        var nodes = second.Nodes.ToDictionary(node => node.Id);
        return first.Nodes.All(node => nodes.TryGetValue(node.Id, out var other) && node.Kind == other.Kind &&
            node.ObjectId == other.ObjectId && node.ParentId == other.ParentId && node.Children.SequenceEqual(other.Children) &&
            node.Enabled == other.Enabled && node.Mirror == other.Mirror && node.Array == other.Array && node.ControlBox == other.ControlBox);
    }

    internal static void Validate(ModifierDocumentState state)
    {
        if (state is null) throw Invalid("The tree snapshot is missing.");
        if (state.SchemaVersion != CurrentSchemaVersion) throw Invalid($"Unsupported tree schema version: {state.SchemaVersion}.");
        if (state.Nodes.Count > MaxNodeCount || state.Roots.Count > MaxNodeCount || state.VisibleInputIds.Count > MaxNodeCount)
            throw Invalid("The saved tree contains too many nodes.");
        var nodes = new Dictionary<Guid, ModifierNodeState>(state.Nodes.Count);
        var sources = new HashSet<Guid>();
        var edgeCount = 0;
        foreach (var node in state.Nodes)
        {
            if (node is null || node.Id == Guid.Empty || !nodes.TryAdd(node.Id, node)) throw Invalid("Duplicate or empty node GUID.");
            if (node.Name is null || !ModifierNames.TryNormalize(node.Name, out var normalizedName, out _) || normalizedName != node.Name)
                throw Invalid("Invalid Modifier name.");
            if (!ModifierSettings.TryValidate(node.Kind, node.Enabled, node.Mirror, node.Array, node.ControlBox, out var settingsError)) throw Invalid(settingsError);
            if (node.ParentId == Guid.Empty) throw Invalid("Empty parent GUID.");
            if (node.Children.Count > MaxNodeCount || (edgeCount += node.Children.Count) > MaxNodeCount)
                throw Invalid("The saved tree contains too many child references.");
            switch (node.Kind)
            {
                case TreeNodeKind.Geometry:
                case TreeNodeKind.BasePlane:
                case TreeNodeKind.ControlBox:
                    if (node.Name.Length != 0) throw Invalid("Geometry names belong to the source object.");
                    if (node.ObjectId is not { } objectId || objectId == Guid.Empty || !sources.Add(objectId))
                        throw Invalid("Duplicate or empty source GUID.");
                    if (node.Children.Count != 0) throw Invalid("A geometry node cannot contain children.");
                    break;
                case TreeNodeKind.BooleanDifference:
                case TreeNodeKind.BooleanUnion:
                case TreeNodeKind.BooleanIntersection:
                case TreeNodeKind.Mirror:
                case TreeNodeKind.Array:
                case TreeNodeKind.Bend:
                    if (node.ObjectId is not null) throw Invalid("A Modifier cannot refer to a source object.");
                    break;
                default:
                    throw Invalid("Unsupported tree node kind.");
            }
        }
        var located = new HashSet<Guid>();
        foreach (var id in state.Roots)
        {
            if (!nodes.TryGetValue(id, out var root) || root.ParentId is not null || !located.Add(id))
                throw Invalid("Invalid, duplicate or parented root.");
        }
        foreach (var parent in state.Nodes)
        {
            var controlCount = 0;
            foreach (var id in parent.Children)
            {
                if (!nodes.TryGetValue(id, out var child) || child.ParentId != parent.Id || !located.Add(id))
                    throw Invalid("Invalid, duplicate or inconsistent child reference.");
                if (child.Kind == TreeNodeKind.BasePlane && (parent.Kind != TreeNodeKind.Mirror || ++controlCount > 1))
                    throw Invalid("A BasePlane must be the single control of its owning Mirror.");
                if (child.Kind == TreeNodeKind.ControlBox && parent.Kind != TreeNodeKind.Bend)
                    throw Invalid("A Control Box must belong to a Bend Modifier.");
            }
        }
        if (state.Nodes.Any(node => node.Kind == TreeNodeKind.BasePlane && node.ParentId is null))
            throw Invalid("A BasePlane cannot exist outside its owning Mirror.");
        if (state.Nodes.Any(node => node.Kind == TreeNodeKind.ControlBox && node.ParentId is null))
            throw Invalid("A Control Box cannot exist outside a Bend Modifier.");
        if (located.Count != nodes.Count) throw Invalid("A tree node has no valid location.");

        // Iterative traversal avoids stack overflow even for a corrupt graph before depth validation.
        var visited = new HashSet<Guid>();
        var pending = new Stack<(Guid Id, int Depth)>(state.Roots.Select(id => (id, 1)));
        while (pending.TryPop(out var item))
        {
            if (item.Depth > MaxTreeDepth) throw Invalid("The saved tree is nested too deeply.");
            if (!visited.Add(item.Id)) throw Invalid("A tree node appears more than once.");
            foreach (var child in nodes[item.Id].Children) pending.Push((child, item.Depth + 1));
        }
        if (visited.Count != nodes.Count) throw Invalid("The saved tree contains a cycle or unreachable nodes.");
        var visible = new HashSet<Guid>();
        foreach (var id in state.VisibleInputIds)
        {
            if (!sources.Contains(id) || !visible.Add(id)) throw Invalid("Invalid or duplicate visible input GUID.");
        }
    }

    private static void Fields(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("Expected a JSON object.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name))
                throw Invalid("Unknown or duplicate saved tree field.");
        }
        if (found.Count != required.Length) throw Invalid("A required saved tree field is missing.");
    }

    private static Guid ReadId(JsonElement element) => element.ValueKind == JsonValueKind.String && element.TryGetGuid(out var id)
        ? id : throw Invalid("Invalid GUID in saved tree.");
    private static Guid? ReadOptionalId(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : ReadId(element);
    private static Guid[] ReadIds(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxNodeCount)
            throw Invalid("Invalid or oversized GUID list.");
        return element.EnumerateArray().Select(ReadId).ToArray();
    }
    private static void WriteIds(Utf8JsonWriter writer, string name, IEnumerable<Guid> ids)
    {
        writer.WriteStartArray(name);
        foreach (var id in ids) writer.WriteStringValue(id);
        writer.WriteEndArray();
    }

    private static void WriteMirror(Utf8JsonWriter writer, MirrorSettings? settings)
    {
        if (settings is null) { writer.WriteNull("mirror"); return; }
        writer.WriteStartObject("mirror");
        WriteVector(writer, "origin", settings.Origin);
        WriteVector(writer, "normal", settings.Normal);
        writer.WriteBoolean("keepOriginal", settings.KeepOriginal);
        writer.WriteBoolean("union", settings.Union);
        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, ArraySettings? settings)
    {
        if (settings is null) { writer.WriteNull("array"); return; }
        writer.WriteStartObject("array");
        writer.WriteNumber("countX", settings.CountX);
        writer.WriteNumber("countY", settings.CountY);
        writer.WriteNumber("countZ", settings.CountZ);
        WriteVector(writer, "spacing", settings.Spacing);
        WriteVector(writer, "axisX", settings.AxisX);
        WriteVector(writer, "axisY", settings.AxisY);
        WriteVector(writer, "axisZ", settings.AxisZ);
        writer.WriteEndObject();
    }

    private static void WriteVector(Utf8JsonWriter writer, string name, ModifierVector value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }

    private static void WriteControlBox(Utf8JsonWriter writer, ControlBoxSettings? settings)
    {
        if (settings is null) { writer.WriteNull("controlBox"); return; }
        writer.WriteStartObject("controlBox");
        writer.WriteNumber("strength", settings.Strength);
        writer.WriteBoolean("limited", settings.Limited);
        writer.WriteEndObject();
    }

    private static ControlBoxSettings? ReadControlBox(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        Fields(element, "strength", "limited");
        if (!element.GetProperty("strength").TryGetDouble(out var strength)) throw Invalid("Invalid Bend Strength.");
        return new ControlBoxSettings(strength, element.GetProperty("limited").GetBoolean());
    }

    private static MirrorSettings? ReadMirror(JsonElement element, int version)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (version < 4) Fields(element, "origin", "normal", "keepOriginal");
        else Fields(element, "origin", "normal", "keepOriginal", "union");
        return new MirrorSettings(ReadVector(element.GetProperty("origin")), ReadVector(element.GetProperty("normal")),
            element.GetProperty("keepOriginal").GetBoolean(), version >= 4 && element.GetProperty("union").GetBoolean());
    }

    private static ArraySettings? ReadArray(JsonElement element, int version)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (version < 4) Fields(element, "countX", "countY", "countZ", "spacing");
        else Fields(element, "countX", "countY", "countZ", "spacing", "axisX", "axisY", "axisZ");
        if (!element.GetProperty("countX").TryGetInt32(out var countX) || !element.GetProperty("countY").TryGetInt32(out var countY) ||
            !element.GetProperty("countZ").TryGetInt32(out var countZ)) throw Invalid("Invalid Array counts.");
        var settings = new ArraySettings { CountX = countX, CountY = countY, CountZ = countZ, Spacing = ReadVector(element.GetProperty("spacing")) };
        return version < 4 ? settings : settings with
        {
            AxisX = ReadVector(element.GetProperty("axisX")),
            AxisY = ReadVector(element.GetProperty("axisY")),
            AxisZ = ReadVector(element.GetProperty("axisZ"))
        };
    }

    private static ModifierVector ReadVector(JsonElement element)
    {
        Fields(element, "x", "y", "z");
        if (!element.GetProperty("x").TryGetDouble(out var x) || !element.GetProperty("y").TryGetDouble(out var y) ||
            !element.GetProperty("z").TryGetDouble(out var z)) throw Invalid("Invalid Modifier coordinates.");
        return new ModifierVector(x, y, z);
    }
    private static FormatException Invalid(string message) => new(message);
}
