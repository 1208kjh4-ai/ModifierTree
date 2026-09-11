namespace ModifierTree.Core;

public enum TreeNodeKind { Geometry, BooleanDifference, BooleanUnion, BooleanIntersection, Mirror, Array, BasePlane, Bend, ControlBox }

public sealed class ModifierTreeNode
{
    internal ModifierTreeNode(TreeNodeKind kind, Guid? objectId = null, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Kind = kind;
        ObjectId = objectId;
        Mirror = kind == TreeNodeKind.Mirror ? MirrorSettings.Default : null;
        Array = kind == TreeNodeKind.Array ? ArraySettings.Default : null;
        ControlBox = kind == TreeNodeKind.ControlBox ? ControlBoxSettings.Default : null;
    }

    public Guid Id { get; }
    public TreeNodeKind Kind { get; }
    public Guid? ObjectId { get; }
    public Guid? ParentId { get; internal set; }
    /// <summary>The user-assigned Modifier name, without its type prefix. Geometry names belong to Rhino.</summary>
    public string Name { get; internal set; } = "";
    public bool Enabled { get; internal set; } = true;
    public MirrorSettings? Mirror { get; internal set; }
    public ArraySettings? Array { get; internal set; }
    public ControlBoxSettings? ControlBox { get; internal set; }
    internal List<Guid> ChildIds { get; } = [];
    public IReadOnlyList<Guid> Children => ChildIds.AsReadOnly();
    public bool IsModifier => Kind is not (TreeNodeKind.Geometry or TreeNodeKind.BasePlane or TreeNodeKind.ControlBox);
    public bool IsControl => Kind is TreeNodeKind.BasePlane or TreeNodeKind.ControlBox;
}

/// <summary>The ordered tree is the expression. A source has exactly one location.</summary>
public sealed class ModifierTreeModel
{
    private Dictionary<Guid, ModifierTreeNode> _nodes = [];
    private Dictionary<Guid, Guid> _sourceNodes = [];
    private List<Guid> _roots = [];
    public IReadOnlyList<Guid> Roots => _roots.AsReadOnly();
    public IEnumerable<ModifierTreeNode> Nodes => _nodes.Values;
    public int SourceCount => _sourceNodes.Count;
    public int ModifierCount => _nodes.Count - SourceCount;
    public long Revision { get; private set; }
    public ModifierTreeNode? Find(Guid id) => _nodes.GetValueOrDefault(id);
    public ModifierTreeNode? FindSource(Guid objectId) => _sourceNodes.TryGetValue(objectId, out var id) ? _nodes[id] : null;

    public bool RegisterSource(Guid objectId)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Source GUID cannot be empty.", nameof(objectId));
        if (_sourceNodes.ContainsKey(objectId)) return false;
        var node = new ModifierTreeNode(TreeNodeKind.Geometry, objectId);
        _nodes.Add(node.Id, node);
        _sourceNodes.Add(objectId, node.Id);
        _roots.Add(node.Id);
        Revision++;
        return true;
    }

    public ModifierTreeNode AddModifier(TreeNodeKind kind = TreeNodeKind.BooleanDifference)
    {
        if (kind is not (TreeNodeKind.BooleanDifference or TreeNodeKind.BooleanUnion or TreeNodeKind.BooleanIntersection or TreeNodeKind.Mirror or TreeNodeKind.Array or TreeNodeKind.Bend))
            throw new ArgumentException("Unsupported modifier.", nameof(kind));
        var node = new ModifierTreeNode(kind);
        _nodes.Add(node.Id, node);
        _roots.Add(node.Id);
        Revision++;
        return node;
    }

    /// <summary>Names are presentation metadata and do not invalidate evaluated geometry.</summary>
    public bool RenameModifier(Guid nodeId, string name, out string error)
    {
        if (!_nodes.TryGetValue(nodeId, out var node) || !node.IsModifier)
        {
            error = "Select a Modifier to rename.";
            return false;
        }
        if (!ModifierNames.TryNormalize(name, out var normalized, out error)) return false;
        node.Name = normalized;
        return true;
    }

    public bool SetModifierEnabled(Guid nodeId, bool enabled, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(nodeId, out var node) || !node.IsModifier)
        { error = "Select a Modifier to enable or disable."; return false; }
        if (node.Enabled == enabled) return true;
        node.Enabled = enabled;
        Revision++;
        return true;
    }

    public bool SetMirrorSettings(Guid nodeId, MirrorSettings settings, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(nodeId, out var node) || node.Kind != TreeNodeKind.Mirror)
        { error = "Select a Mirror Modifier."; return false; }
        if (!ModifierSettings.TryValidate(settings, out error)) return false;
        if (node.Mirror == settings) return true;
        node.Mirror = settings;
        Revision++;
        return true;
    }

    public bool SetArraySettings(Guid nodeId, ArraySettings settings, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(nodeId, out var node) || node.Kind != TreeNodeKind.Array)
        { error = "Select an Array Modifier."; return false; }
        if (!ModifierSettings.TryValidate(settings, out error)) return false;
        if (node.Array == settings) return true;
        node.Array = settings;
        Revision++;
        return true;
    }

    public bool SetControlBoxSettings(Guid nodeId, ControlBoxSettings settings, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(nodeId, out var node) || node.Kind != TreeNodeKind.ControlBox)
        { error = "Select a Control Box."; return false; }
        if (!ModifierSettings.TryValidate(settings, out error)) return false;
        var normalized = settings.Strength == 0 ? settings with { Strength = 0 } : settings;
        if (node.ControlBox == normalized) return true;
        node.ControlBox = normalized;
        Revision++;
        return true;
    }

    /// <summary>Adds an independently editable control to a Bend. Control order determines deformation order.</summary>
    public bool AddControlBox(Guid bendNodeId, Guid objectId, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(bendNodeId, out var bend) || bend.Kind != TreeNodeKind.Bend)
        { error = "Select a Bend Modifier."; return false; }
        if (objectId == Guid.Empty || _sourceNodes.ContainsKey(objectId))
        { error = "The Control Box must have its own unregistered Rhino object."; return false; }
        if (_nodes.Count >= TreeStateCodec.MaxNodeCount)
        { error = "Adding a Control Box would exceed the saved tree node limit."; return false; }
        var depth = 2;
        for (var parentId = bend.ParentId; parentId is { } parent; parentId = _nodes[parent].ParentId) depth++;
        if (depth > TreeStateCodec.MaxTreeDepth)
        { error = "Adding a Control Box would exceed the saved tree depth limit."; return false; }
        var control = new ModifierTreeNode(TreeNodeKind.ControlBox, objectId) { ParentId = bend.Id };
        var lastControl = bend.ChildIds.FindLastIndex(id => _nodes[id].Kind == TreeNodeKind.ControlBox);
        _nodes.Add(control.Id, control);
        _sourceNodes.Add(objectId, control.Id);
        bend.ChildIds.Insert(lastControl + 1, control.Id);
        Revision++;
        return true;
    }

    /// <summary>Registers the owned Mirror control without making it a geometric input.</summary>
    public bool SetBasePlane(Guid mirrorNodeId, Guid objectId, out string error)
    {
        error = "";
        if (!_nodes.TryGetValue(mirrorNodeId, out var mirror) || mirror.Kind != TreeNodeKind.Mirror)
        { error = "Select a Mirror Modifier."; return false; }
        if (objectId == Guid.Empty)
        { error = "The BasePlane object GUID cannot be empty."; return false; }
        var previous = mirror.ChildIds.Select(id => _nodes[id]).SingleOrDefault(node => node.IsControl);
        if (previous?.ObjectId == objectId) return true;
        if (_sourceNodes.ContainsKey(objectId))
        { error = "The BasePlane must have its own unregistered Rhino object."; return false; }
        if (previous is null && _nodes.Count >= TreeStateCodec.MaxNodeCount)
        { error = "Adding a BasePlane would exceed the saved tree node limit."; return false; }
        var depth = 2;
        for (var parentId = mirror.ParentId; parentId is { } parent; parentId = _nodes[parent].ParentId) depth++;
        if (depth > TreeStateCodec.MaxTreeDepth)
        { error = "Adding a BasePlane would exceed the saved tree depth limit."; return false; }
        var control = new ModifierTreeNode(TreeNodeKind.BasePlane, objectId, previous?.Id) { ParentId = mirror.Id };
        if (previous is not null)
        {
            _sourceNodes.Remove(previous.ObjectId!.Value);
            _nodes[control.Id] = control;
        }
        else
        {
            _nodes.Add(control.Id, control);
            mirror.ChildIds.Insert(0, control.Id);
        }
        _sourceNodes.Add(objectId, control.Id);
        Revision++;
        return true;
    }

    /// <summary>Copies a subtree immediately after itself using independent, caller-provided source GUIDs.</summary>
    public ModifierTreeNode CloneSubtree(Guid nodeId, IReadOnlyDictionary<Guid, Guid> sourceObjectMap)
    {
        ArgumentNullException.ThrowIfNull(sourceObjectMap);
        if (!_nodes.TryGetValue(nodeId, out var root)) throw new ArgumentException("Select an existing subtree to duplicate.", nameof(nodeId));
        if (root.IsControl) throw new ArgumentException("An owned control can only be duplicated with its Modifier.", nameof(nodeId));
        var originals = new List<ModifierTreeNode>();
        var pending = new Stack<Guid>();
        pending.Push(nodeId);
        while (pending.TryPop(out var id))
        {
            var node = _nodes[id];
            originals.Add(node);
            for (var index = node.ChildIds.Count - 1; index >= 0; index--) pending.Push(node.ChildIds[index]);
        }
        if (_nodes.Count + originals.Count > TreeStateCodec.MaxNodeCount)
            throw new ArgumentException("Duplicating this subtree would exceed the saved tree node limit.", nameof(nodeId));
        var sourceIds = originals.Where(node => node.ObjectId.HasValue).Select(node => node.ObjectId!.Value).ToHashSet();
        var sourceMap = sourceObjectMap.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (sourceMap.Count != sourceIds.Count || sourceMap.Keys.Any(id => !sourceIds.Contains(id)) ||
            sourceIds.Any(id => !sourceMap.ContainsKey(id)) || sourceMap.Values.Any(id => id == Guid.Empty || _sourceNodes.ContainsKey(id)) ||
            sourceMap.Values.Distinct().Count() != sourceMap.Count)
            throw new ArgumentException("Every subtree source needs a unique, new source GUID and no extra mappings.", nameof(sourceObjectMap));

        var usedIds = _nodes.Keys.ToHashSet();
        var nodeMap = new Dictionary<Guid, Guid>();
        foreach (var original in originals)
        {
            Guid copyId;
            do { copyId = Guid.NewGuid(); } while (!usedIds.Add(copyId));
            nodeMap.Add(original.Id, copyId);
        }
        var copies = originals.Select(original => new ModifierTreeNode(original.Kind,
            original.ObjectId is { } objectId ? sourceMap[objectId] : null, nodeMap[original.Id])
        {
            ParentId = original.Id == nodeId ? root.ParentId : nodeMap[original.ParentId!.Value],
            Name = original.Name,
            Enabled = original.Enabled,
            Mirror = original.Mirror,
            Array = original.Array,
            ControlBox = original.ControlBox
        }).ToArray();
        for (var index = 0; index < copies.Length; index++)
            copies[index].ChildIds.AddRange(originals[index].ChildIds.Select(id => nodeMap[id]));
        var siblings = MutableChildren(root.ParentId);
        var insertionIndex = siblings.IndexOf(nodeId) + 1;
        var replacement = siblings.ToList();
        replacement.Insert(insertionIndex, copies[0].Id);

        // Validate the complete proposed graph before changing live collections or source registrations.
        var proposed = _nodes.Values.Concat(copies).Select(node => new ModifierNodeState(node.Id, node.Kind, node.ObjectId,
            node.ParentId, node.Id == root.ParentId ? replacement : node.Children, node.Name, node.Enabled, node.Mirror, node.Array, node.ControlBox));
        TreeStateCodec.Validate(new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion,
            root.ParentId is null ? replacement : _roots, proposed, true, []));
        foreach (var copy in copies)
        {
            _nodes.Add(copy.Id, copy);
            if (copy.ObjectId is { } objectId) _sourceNodes.Add(objectId, copy.Id);
        }
        siblings.Insert(insertionIndex, copies[0].Id);
        Revision++;
        return copies[0];
    }

    /// <summary>Replaces a complete Modifier subtree with a newly registered source at the same location.</summary>
    public ModifierTreeNode ReplaceSubtreeWithSource(Guid nodeId, Guid objectId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node) || !node.IsModifier)
            throw new ArgumentException("Select a Modifier to replace.", nameof(nodeId));
        if (objectId == Guid.Empty || _sourceNodes.ContainsKey(objectId))
            throw new ArgumentException("The replacement must have a new, nonempty source GUID.", nameof(objectId));

        // Collect the complete removal set before committing; siblings and ancestors retain their IDs.
        var removed = new List<ModifierTreeNode>();
        var pending = new Stack<Guid>();
        pending.Push(nodeId);
        while (pending.TryPop(out var id))
        {
            var descendant = _nodes[id];
            removed.Add(descendant);
            foreach (var child in descendant.Children) pending.Push(child);
        }
        var replacement = new ModifierTreeNode(TreeNodeKind.Geometry, objectId) { ParentId = node.ParentId };
        var siblings = MutableChildren(node.ParentId);
        var index = siblings.IndexOf(nodeId);
        foreach (var descendant in removed)
        {
            _nodes.Remove(descendant.Id);
            if (descendant.ObjectId is { } sourceId) _sourceNodes.Remove(sourceId);
        }
        _nodes.Add(replacement.Id, replacement);
        _sourceNodes.Add(objectId, replacement.Id);
        siblings[index] = replacement.Id;
        Revision++;
        return replacement;
    }

    public IReadOnlyList<Guid> ChildrenOf(Guid? parentId) => parentId is { } id ? _nodes[id].Children : Roots;

    /// <summary>Owned source GUIDs, in tree order, without siblings or ancestors.</summary>
    public IReadOnlyList<Guid> SourcesInSubtree(Guid nodeId)
    {
        if (!_nodes.ContainsKey(nodeId)) return Array.Empty<Guid>();
        var sources = new List<Guid>();
        void Visit(Guid id)
        {
            var node = _nodes[id];
            if (node.ObjectId is { } objectId) sources.Add(objectId);
            else foreach (var child in node.Children) Visit(child);
        }
        Visit(nodeId);
        return sources;
    }

    /// <summary>Geometric input GUIDs only, excluding owned controls such as Mirror BasePlane.</summary>
    public IReadOnlyList<Guid> GeometrySourcesInSubtree(Guid nodeId) => SourcesInSubtree(nodeId)
        .Where(id => FindSource(id)!.Kind == TreeNodeKind.Geometry).ToArray();

    /// <summary>Returns selection roots in visual tree order, ignoring duplicates and descendants of selected ancestors.</summary>
    /// <exception cref="ArgumentException">A requested node does not exist.</exception>
    public IReadOnlyList<Guid> NormalizeMoveNodes(IEnumerable<Guid> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var selected = nodeIds.ToHashSet();
        if (selected.Any(id => !_nodes.ContainsKey(id)))
            throw new ArgumentException("A dragged item no longer exists.", nameof(nodeIds));

        var ordered = new List<Guid>(selected.Count);
        var pending = new Stack<Guid>(_roots.AsEnumerable().Reverse());
        while (pending.TryPop(out var id))
        {
            if (selected.Contains(id))
            {
                ordered.Add(id);
                continue; // Its descendants move with it and must not be detached separately.
            }
            var children = _nodes[id].ChildIds;
            for (var index = children.Count - 1; index >= 0; index--) pending.Push(children[index]);
        }
        return ordered.AsReadOnly();
    }

    public bool CanMove(Guid nodeId, Guid? parentId, int insertionIndex, out string error) =>
        CanMoveMany([nodeId], parentId, insertionIndex, out error);

    public bool CanMoveMany(IEnumerable<Guid> nodeIds, Guid? parentId, int insertionIndex, out string error) =>
        TryPrepareMove(nodeIds, parentId, insertionIndex, out _, out error);

    // insertionIndex refers to the destination list BEFORE removing any dragged nodes.
    public bool Move(Guid nodeId, Guid? parentId, int insertionIndex, out string error) =>
        MoveMany([nodeId], parentId, insertionIndex, out error);

    /// <summary>Moves the complete selection atomically, preserving tree order and invalidating geometry at most once.</summary>
    public bool MoveMany(IEnumerable<Guid> nodeIds, Guid? parentId, int insertionIndex, out string error)
    {
        if (!TryPrepareMove(nodeIds, parentId, insertionIndex, out var ordered, out error)) return false;
        var selected = ordered.ToHashSet();
        var destination = MutableChildren(parentId);
        var adjustedIndex = insertionIndex - destination.Take(insertionIndex).Count(selected.Contains);

        // Prepare every affected sibling list before committing so rejected or unchanged drops touch no state.
        var parents = ordered.Select(id => _nodes[id].ParentId).Append(parentId).Distinct();
        var replacements = new List<(List<Guid> Existing, List<Guid> Replacement)>();
        foreach (var parent in parents)
        {
            var existing = MutableChildren(parent);
            var replacement = existing.Where(id => !selected.Contains(id)).ToList();
            if (parent == parentId) replacement.InsertRange(adjustedIndex, ordered);
            replacements.Add((existing, replacement));
        }
        if (replacements.All(pair => pair.Existing.SequenceEqual(pair.Replacement))) return true;

        foreach (var (existing, replacement) in replacements)
        {
            existing.Clear();
            existing.AddRange(replacement);
        }
        foreach (var id in ordered) _nodes[id].ParentId = parentId;
        Revision++;
        return true;
    }

    private bool TryPrepareMove(IEnumerable<Guid> nodeIds, Guid? parentId, int insertionIndex,
        out IReadOnlyList<Guid> ordered, out string error)
    {
        ordered = Array.Empty<Guid>();
        error = "";
        try { ordered = NormalizeMoveNodes(nodeIds); }
        catch (ArgumentException) { error = "A dragged item no longer exists."; return false; }
        if (ordered.Count == 0) { error = "Select an item to move."; return false; }
        if (ordered.Any(id => _nodes[id].Kind == TreeNodeKind.BasePlane && _nodes[id].ParentId != parentId))
        { error = "A BasePlane must remain inside its owning Mirror."; return false; }
        if (ordered.Any(id => _nodes[id].Kind == TreeNodeKind.ControlBox) &&
            (parentId is not { } bendId || !_nodes.TryGetValue(bendId, out var bend) || bend.Kind != TreeNodeKind.Bend))
        { error = "A Control Box must remain inside a Bend Modifier."; return false; }
        if (parentId is { } parent && (!_nodes.TryGetValue(parent, out var target) || !target.IsModifier))
        { error = "Only a Modifier can contain inputs."; return false; }
        var selected = ordered.ToHashSet();
        for (var ancestor = parentId; ancestor is { } id; ancestor = _nodes[id].ParentId)
        {
            if (selected.Contains(id))
            { error = "An item cannot be placed inside itself or one of its children."; return false; }
        }
        if (insertionIndex < 0 || insertionIndex > ChildrenOf(parentId).Count)
        { error = "The drop position is no longer valid."; return false; }
        return true;
    }

    /// <summary>Removing a Modifier promotes geometric children and unregisters its owned controls; native objects are never deleted.</summary>
    public bool Remove(Guid nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node)) return false;
        if (node.Kind == TreeNodeKind.BasePlane) return false;
        var siblings = MutableChildren(node.ParentId);
        var index = siblings.IndexOf(nodeId);
        siblings.RemoveAt(index);
        foreach (var child in node.ChildIds)
        {
            if (_nodes[child].IsControl)
            {
                _sourceNodes.Remove(_nodes[child].ObjectId!.Value);
                _nodes.Remove(child);
                continue;
            }
            _nodes[child].ParentId = node.ParentId;
            siblings.Insert(index++, child);
        }
        if (node.ObjectId is { } objectId) _sourceNodes.Remove(objectId);
        _nodes.Remove(nodeId);
        Revision++;
        return true;
    }

    // Called only after the codec validates the complete graph. Build replacements before committing.
    internal void RestoreValidated(ModifierDocumentState state)
    {
        var nodes = new Dictionary<Guid, ModifierTreeNode>(state.Nodes.Count);
        var sources = new Dictionary<Guid, Guid>();
        var roots = new List<Guid>(state.Roots);
        foreach (var saved in state.Nodes)
        {
            var node = new ModifierTreeNode(saved.Kind, saved.ObjectId, saved.Id)
            {
                ParentId = saved.ParentId, Name = saved.Name, Enabled = saved.Enabled, Mirror = saved.Mirror, Array = saved.Array,
                ControlBox = saved.ControlBox
            };
            node.ChildIds.AddRange(saved.Children);
            nodes.Add(node.Id, node);
            if (node.ObjectId is { } objectId) sources.Add(objectId, node.Id);
        }
        _nodes = nodes;
        _sourceNodes = sources;
        _roots = roots;
        Revision++;
    }

    internal void RestoreMetadataValidated(ModifierDocumentState state)
    {
        foreach (var saved in state.Nodes) _nodes[saved.Id].Name = saved.Name;
    }

    private List<Guid> MutableChildren(Guid? parentId) => parentId is { } id ? _nodes[id].ChildIds : _roots;
}
