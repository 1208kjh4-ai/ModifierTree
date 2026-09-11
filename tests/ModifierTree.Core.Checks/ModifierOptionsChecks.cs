using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class ModifierOptionsChecks
{
    public static void Verify()
    {
        OptionsAndRevision();
        PersistenceAndMetadata();
        Cloning();
        CloneLimits();
    }

    private static void OptionsAndRevision()
    {
        var tree = new ModifierTreeModel();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        var boolean = tree.AddModifier();
        var sourceId = Guid.NewGuid();
        tree.RegisterSource(sourceId);
        var source = tree.FindSource(sourceId)!;
        Check(mirror.Enabled && mirror.Mirror == MirrorSettings.Default && mirror.Array is null &&
              array.Array == ArraySettings.Default && array.Mirror is null && boolean.Enabled,
            "New Modifiers start enabled with immutable settings belonging only to their kind");
        tree.RenameModifier(mirror.Id, "Left", out _);
        tree.RenameModifier(array.Id, "Grid", out _);
        Check(ModifierNames.DisplayName(mirror) == "(Mirror) Left" && ModifierNames.DisplayName(array) == "(Array) Grid" &&
              ModifierNames.ResultName(mirror) == "Left", "Mirror and Array names use readable type prefixes and plain result names");

        var revision = tree.Revision;
        Check(tree.SetModifierEnabled(mirror.Id, false, out _) && !mirror.Enabled && tree.Revision == revision + 1 &&
              ReferenceEquals(tree.Find(mirror.Id), mirror), "Turning a Modifier off changes geometry revision once while preserving its node instance");
        revision = tree.Revision;
        Check(tree.SetModifierEnabled(mirror.Id, false, out _) && tree.Revision == revision &&
              !tree.SetModifierEnabled(source.Id, false, out _) && !tree.SetModifierEnabled(Guid.NewGuid(), false, out _),
            "Repeated ON/OFF and rejected non-Modifier toggles leave geometry revision unchanged");

        var mirrorSettings = new MirrorSettings(new ModifierVector(3, -2, 1), new ModifierVector(1, 2, 0), false);
        Check(tree.SetMirrorSettings(mirror.Id, mirrorSettings, out _) && mirror.Mirror == mirrorSettings && tree.Revision == revision + 1 &&
              !mirror.Enabled && ReferenceEquals(tree.Find(mirror.Id), mirror),
            "Editing a disabled Mirror stores its world plane and original-copy choice and invalidates geometry once");
        revision = tree.Revision;
        var arraySettings = new ArraySettings { CountX = 3, CountY = 2, CountZ = 2, Spacing = new ModifierVector(-5, 8, 12) };
        Check(tree.SetArraySettings(array.Id, arraySettings, out _) && array.Array == arraySettings && tree.Revision == revision + 1,
            "Rectangular Array stores independent XYZ counts and signed document-unit spacing");
        revision = tree.Revision;
        Check(tree.SetMirrorSettings(mirror.Id, mirrorSettings with { }, out _) && tree.SetArraySettings(array.Id, arraySettings with { }, out _) &&
              tree.RenameModifier(mirror.Id, "Other", out _) && tree.Revision == revision,
            "Value-equal settings and name edits retain geometry caches");

        var badMirrors = new[]
        {
            mirrorSettings with { Normal = ModifierVector.Zero },
            mirrorSettings with { Normal = new ModifierVector(double.NaN, 1, 0) },
            mirrorSettings with { Origin = new ModifierVector(0, double.PositiveInfinity, 0) },
            mirrorSettings with { Origin = new ModifierVector(ModifierSettings.MaxCoordinate + 1, 0, 0) }
        };
        foreach (var bad in badMirrors) Check(!tree.SetMirrorSettings(mirror.Id, bad, out _) && mirror.Mirror == mirrorSettings && tree.Revision == revision,
            "Invalid Mirror plane is rejected before changing node settings or revision");
        var badArrays = new[]
        {
            arraySettings with { CountX = 0 }, arraySettings with { CountY = -1 },
            arraySettings with { CountX = 256, CountY = 2 }, arraySettings with { CountZ = int.MaxValue },
            arraySettings with { Spacing = new ModifierVector(0, double.NaN, 0) }
        };
        foreach (var bad in badArrays) Check(!tree.SetArraySettings(array.Id, bad, out _) && array.Array == arraySettings && tree.Revision == revision,
            "Invalid Array count, total placement limit or spacing is rejected atomically");
        Check(!tree.SetMirrorSettings(array.Id, mirrorSettings, out _) && !tree.SetArraySettings(mirror.Id, arraySettings, out _) &&
              !tree.SetMirrorSettings(mirror.Id, null!, out _) && !tree.SetArraySettings(array.Id, null!, out _) && tree.Revision == revision,
            "Settings cannot be assigned to another Modifier kind or replaced with null");
        Check(ModifierSettings.TryValidate(new ArraySettings { CountX = 8, CountY = 8, CountZ = 4 }, out _) &&
              ModifierSettings.TryValidate(new ArraySettings { CountX = 1, CountY = 1, CountZ = 1, Spacing = ModifierVector.Zero }, out _),
            "The placement limit includes the original and allows one-copy and three-dimensional arrays");
    }

    private static void PersistenceAndMetadata()
    {
        var tree = new ModifierTreeModel();
        var visibility = new InputVisibility();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        Move(tree, array.Id, mirror.Id, 0);
        tree.SetMirrorSettings(mirror.Id, new MirrorSettings(new ModifierVector(7, 8, 9), new ModifierVector(0, 1, 0), false), out _);
        tree.SetArraySettings(array.Id, new ArraySettings { CountX = 3, CountY = 4, CountZ = 1, Spacing = new ModifierVector(7, -8, 0) }, out _);
        tree.SetModifierEnabled(array.Id, false, out _);
        var original = TreeStateCodec.Capture(tree, visibility, true);
        var json = TreeStateCodec.Encode(original);
        var saved = TreeStateCodec.Decode(json);
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(saved, restored, new InputVisibility());
        Check(saved.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && TreeStateCodec.Encode(saved) == json && restored.Find(mirror.Id)!.Mirror == mirror.Mirror &&
              restored.Find(array.Id)!.Array == array.Array && !restored.Find(array.Id)!.Enabled && TreeStateCodec.SameGeometry(original, saved),
            "Current schema deterministically restores ordered Mirror/Array settings and disabled state");

        tree.RenameModifier(mirror.Id, "Updated", out _);
        var metadata = TreeStateCodec.Capture(tree, visibility, false);
        Check(TreeStateCodec.SameGeometry(original, metadata), "Geometry comparison ignores user names and preview visibility");
        var revision = restored.Revision;
        var restoredMirror = restored.Find(mirror.Id)!;
        TreeStateCodec.ApplyMetadata(metadata, restored, new InputVisibility());
        Check(restored.Revision == revision && ReferenceEquals(restoredMirror, restored.Find(mirror.Id)) && restoredMirror.Name == "Updated",
            "Metadata restore retains Mirror settings, node identity and geometry revision");

        tree.SetModifierEnabled(array.Id, true, out _);
        var enabled = TreeStateCodec.Capture(tree, visibility, true);
        Check(!TreeStateCodec.SameGeometry(original, enabled), "ON/OFF changes are classified as geometry edits for native Undo and preview refresh");
        ExpectFormat(() => TreeStateCodec.ApplyMetadata(enabled, restored, visibility));
        tree.SetModifierEnabled(array.Id, false, out _);
        tree.SetMirrorSettings(mirror.Id, mirror.Mirror! with { KeepOriginal = true }, out _);
        var changedPlane = TreeStateCodec.Capture(tree, visibility, true);
        Check(!TreeStateCodec.SameGeometry(original, changedPlane), "Mirror plane and original-copy settings invalidate geometry snapshot equality");
        ExpectFormat(() => TreeStateCodec.ApplyMetadata(changedPlane, restored, visibility));
        tree.SetMirrorSettings(mirror.Id, original.Nodes.Single(node => node.Id == mirror.Id).Mirror!, out _);
        tree.SetArraySettings(array.Id, array.Array! with { CountX = 2 }, out _);
        var changedArray = TreeStateCodec.Capture(tree, visibility, true);
        Check(!TreeStateCodec.SameGeometry(original, changedArray), "Array count and spacing changes invalidate geometry snapshot equality");
        ExpectFormat(() => TreeStateCodec.ApplyMetadata(changedArray, restored, visibility));
        Check(restored.Revision == revision && !restored.Find(array.Id)!.Enabled && restored.Find(array.Id)!.Array == saved.Nodes.Single(node => node.Id == array.Id).Array,
            "Rejected metadata application leaves existing enabled state and transform settings untouched");

        var legacyTree = new ModifierTreeModel();
        var oldModifier = legacyTree.AddModifier(TreeNodeKind.BooleanUnion);
        legacyTree.RenameModifier(oldModifier.Id, "Legacy", out _);
        foreach (var version in new[] { 1, 2 })
        {
            var legacy = JsonNode.Parse(TreeStateCodec.Encode(TreeStateCodec.Capture(legacyTree, visibility, true)))!.AsObject();
            legacy["schemaVersion"] = version;
            foreach (var item in legacy["nodes"]!.AsArray())
            {
                var node = item!.AsObject();
                node.Remove("enabled"); node.Remove("mirror"); node.Remove("array"); node.Remove("controlBox");
                if (version == 1) node.Remove("name");
            }
            var migrated = TreeStateCodec.Decode(legacy.ToJsonString());
            Check(migrated.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && migrated.Nodes.Single().Enabled && migrated.Nodes.Single().Mirror is null &&
                  migrated.Nodes.Single().Array is null && migrated.Nodes.Single().Name == (version == 1 ? "" : "Legacy"),
                $"Schema {version} migrates to enabled Modifiers without changing historical names or kind");
        }

        void Reject(Action<JsonObject, JsonObject> mutate)
        {
            var document = JsonNode.Parse(json)!.AsObject();
            var node = document["nodes"]!.AsArray().Single(item => item!["kind"]!.GetValue<string>() == "Mirror")!.AsObject();
            mutate(document, node);
            ExpectFormat(() => TreeStateCodec.Decode(document.ToJsonString()));
        }
        Reject((_, node) => node.Remove("enabled"));
        Reject((_, node) => node["enabled"] = 0);
        Reject((_, node) => node["mirror"] = null);
        Reject((_, node) => node["mirror"]!["normal"]!["x"] = "NaN");
        Reject((_, node) => node["mirror"]!["origin"]!["x"] = 2e12);
        Reject((_, node) => node["mirror"]!["normal"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 });
        Reject((_, node) => node["mirror"]!["futureSetting"] = true);
        Reject((_, node) => node["mirror"]!["origin"]!["w"] = 1);
        Reject((_, node) => node["array"] = new JsonObject { ["countX"] = 2, ["countY"] = 1, ["countZ"] = 1,
            ["spacing"] = new JsonObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 } });
        Reject((document, _) => document["nodes"]!.AsArray().Single(node => node!["kind"]!.GetValue<string>() == "Array")!["array"]!["countX"] = 256);
        Check(true, "Saved settings reject missing, malformed, unknown, out-of-range and wrong-kind fields without silently defaulting");
        ExpectFormat(() => TreeStateCodec.Decode(json.Replace("\"keepOriginal\":false", "\"keepOriginal\":false,\"keepOriginal\":false")));
        Check(true, "Duplicate fields inside Modifier settings are rejected like duplicate graph fields");
    }

    private static void Cloning()
    {
        var tree = new ModifierTreeModel();
        var visibility = new InputVisibility();
        var objectIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in objectIds) tree.RegisterSource(id);
        var sources = objectIds.Select(id => tree.FindSource(id)!).ToArray();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        var outer = tree.AddModifier(TreeNodeKind.BooleanUnion);
        tree.RenameModifier(mirror.Id, "Left", out _);
        tree.RenameModifier(array.Id, "Grid", out _);
        tree.SetModifierEnabled(mirror.Id, false, out _);
        tree.SetArraySettings(array.Id, new ArraySettings { CountX = 3, Spacing = new ModifierVector(-12, 0, 0) }, out _);
        Move(tree, sources[0].Id, mirror.Id, 0);
        Move(tree, mirror.Id, array.Id, 0);
        Move(tree, sources[1].Id, array.Id, 1);
        Move(tree, array.Id, outer.Id, 0);
        Move(tree, sources[2].Id, outer.Id, 1);
        var before = TreeStateCodec.Capture(tree, visibility, true);
        var revision = tree.Revision;
        var mapping = objectIds.Take(2).ToDictionary(id => id, _ => Guid.NewGuid());
        var clone = tree.CloneSubtree(array.Id, mapping);
        var cloneMirror = tree.Find(clone.Children[0])!;
        Check(clone.Id != array.Id && clone.ParentId == outer.Id && outer.Children.SequenceEqual(new[] { array.Id, clone.Id, sources[2].Id }) &&
              tree.Revision == revision + 1, "Nested subtree duplication inserts one independent sibling immediately after the original and revises geometry once");
        Check(tree.SourcesInSubtree(clone.Id).SequenceEqual(objectIds.Take(2).Select(id => mapping[id])) &&
              !before.Nodes.Select(node => node.Id).Intersect(new[] { clone.Id, cloneMirror.Id }.Concat(clone.Children.Skip(1)).Concat(cloneMirror.Children)).Any(),
            "Every duplicated node receives a fresh identity and every source uses its mapped independent Rhino GUID in original order");
        Check(clone.Name == "Grid" && clone.Array == array.Array && cloneMirror.Name == "Left" && !cloneMirror.Enabled &&
              cloneMirror.Mirror == mirror.Mirror && ReferenceEquals(tree.Find(array.Id), array) && ReferenceEquals(tree.Find(outer.Id), outer),
            "Duplication preserves names, enabled state, transform settings and original node identities");
        tree.SetModifierEnabled(cloneMirror.Id, true, out _);
        tree.SetArraySettings(clone.Id, clone.Array! with { CountX = 4 }, out _);
        Check(!mirror.Enabled && array.Array!.CountX == 3 && clone.Array!.CountX == 4,
            "Editing a duplicated subtree does not change the original settings");

        var unchanged = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true));
        revision = tree.Revision;
        var invalidMaps = new[]
        {
            new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, Guid> { [objectIds[0]] = Guid.NewGuid() },
            new Dictionary<Guid, Guid> { [objectIds[0]] = Guid.Empty, [objectIds[1]] = Guid.NewGuid() },
            new Dictionary<Guid, Guid> { [objectIds[0]] = objectIds[2], [objectIds[1]] = Guid.NewGuid() },
            new Dictionary<Guid, Guid> { [objectIds[0]] = mapping[objectIds[0]], [objectIds[1]] = Guid.NewGuid() },
            new Dictionary<Guid, Guid> { [objectIds[0]] = Guid.NewGuid(), [objectIds[1]] = Guid.NewGuid(), [objectIds[2]] = Guid.NewGuid() }
        };
        var repeated = Guid.NewGuid();
        foreach (var map in invalidMaps.Append(new Dictionary<Guid, Guid> { [objectIds[0]] = repeated, [objectIds[1]] = repeated }))
            ExpectArgument(() => tree.CloneSubtree(array.Id, map));
        ExpectArgument(() => tree.CloneSubtree(Guid.NewGuid(), new Dictionary<Guid, Guid>()));
        Check(tree.Revision == revision && TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true)) == unchanged,
            "Missing, extra, reused, empty or duplicate source mappings reject the entire clone before any tree changes");

        var empty = tree.AddModifier(TreeNodeKind.Mirror);
        var emptyClone = tree.CloneSubtree(empty.Id, new Dictionary<Guid, Guid>());
        Check(emptyClone.Children.Count == 0 && tree.Roots.SequenceEqual(new[] { outer.Id, empty.Id, emptyClone.Id }),
            "Empty Modifiers can be duplicated at root without creating source objects");
        var directSource = tree.CloneSubtree(sources[2].Id, new Dictionary<Guid, Guid> { [objectIds[2]] = Guid.NewGuid() });
        Check(directSource.Kind == TreeNodeKind.Geometry && directSource.ParentId == outer.Id && outer.Children[^1] == directSource.Id,
            "The clone primitive also supports a single source while retaining its exact sibling position");
    }

    private static void CloneLimits()
    {
        var tree = new ModifierTreeModel();
        var node = tree.AddModifier();
        for (var index = 1; index < TreeStateCodec.MaxNodeCount; index++) tree.AddModifier();
        var revision = tree.Revision;
        ExpectArgument(() => tree.CloneSubtree(node.Id, new Dictionary<Guid, Guid>()));
        Check(tree.Revision == revision && tree.ModifierCount == TreeStateCodec.MaxNodeCount,
            "Duplication rejects the persistence node budget before adding any copy");

        var deep = new ModifierTreeModel();
        var root = deep.AddModifier();
        var parent = root;
        for (var index = 1; index <= TreeStateCodec.MaxTreeDepth; index++)
        {
            var child = deep.AddModifier();
            Move(deep, child.Id, parent.Id, 0);
            parent = child;
        }
        revision = deep.Revision;
        ExpectFormat(() => deep.CloneSubtree(root.Id, new Dictionary<Guid, Guid>()));
        Check(deep.Revision == revision && deep.ModifierCount == TreeStateCodec.MaxTreeDepth + 1 && deep.Roots.SequenceEqual(new[] { root.Id }),
            "Duplication validates complete graph depth before committing cloned nodes");
    }

    private static void Move(ModifierTreeModel tree, Guid id, Guid? parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }
    private static void ExpectFormat(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new Exception("FAIL: Invalid Modifier state did not throw FormatException.");
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("FAIL: Invalid clone did not throw ArgumentException.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
