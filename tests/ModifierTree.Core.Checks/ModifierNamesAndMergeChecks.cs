using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class ModifierNamesAndMergeChecks
{
    public static void Verify()
    {
        NamesAndMigration();
        Replacement();
    }

    private static void NamesAndMigration()
    {
        var tree = new ModifierTreeModel();
        var visibility = new InputVisibility();
        var sourceId = Guid.NewGuid();
        tree.RegisterSource(sourceId);
        var source = tree.FindSource(sourceId)!;
        var modifier = tree.AddModifier();
        Move(tree, source.Id, modifier.Id, 0);
        Check(modifier.Name == "" && ModifierNames.DisplayName(modifier) == "Boolean Difference" &&
              ModifierNames.ResultName(modifier) == "Boolean Difference", "Unnamed Modifiers keep their full type name and usable result name");
        var revision = tree.Revision;
        Check(tree.RenameModifier(modifier.Id, "  Main 본체  ", out _) && modifier.Name == "Main 본체" && tree.Revision == revision &&
              ModifierNames.DisplayName(modifier) == "(BD) Main 본체" && ModifierNames.ResultName(modifier) == "Main 본체",
            "Modifier rename trims Unicode names and derives its display prefix without invalidating geometry");
        Check(!tree.RenameModifier(source.Id, "Source", out _) && !tree.RenameModifier(Guid.NewGuid(), "Missing", out _) &&
              !tree.RenameModifier(modifier.Id, "Main\nOther", out _) && !tree.RenameModifier(modifier.Id, new string('x', 121), out _) &&
              !tree.RenameModifier(modifier.Id, "Main\u2028Other", out _) &&
              tree.Revision == revision && modifier.Name == "Main 본체", "Invalid rename attempts preserve the existing name and tree");

        var state = TreeStateCodec.Capture(tree, visibility, true);
        var encoded = TreeStateCodec.Encode(state);
        var saved = TreeStateCodec.Decode(encoded);
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(saved, restored, new InputVisibility());
        Check(saved.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && restored.Find(modifier.Id)!.Name == "Main 본체" && TreeStateCodec.Encode(saved) == encoded,
            "Current schema preserves custom Modifier names and produces deterministic JSON");

        var legacyJson = JsonNode.Parse(encoded)!.AsObject();
        legacyJson["schemaVersion"] = 1;
        foreach (var node in legacyJson["nodes"]!.AsArray())
        {
            node!.AsObject().Remove("name");
            node.AsObject().Remove("enabled");
            node.AsObject().Remove("mirror");
            node.AsObject().Remove("array");
            node.AsObject().Remove("controlBox");
        }
        var migrated = TreeStateCodec.Decode(legacyJson.ToJsonString());
        Check(migrated.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && migrated.Nodes.All(node => node.Name == "") &&
              migrated.Roots.SequenceEqual(state.Roots) && migrated.Nodes.Single(node => node.Id == modifier.Id).Children.SequenceEqual(modifier.Children),
            "Schema 1 files migrate to empty custom names while retaining saved IDs, hierarchy and settings");
        legacyJson["roots"] = new JsonArray();
        ExpectFormat(() => TreeStateCodec.Decode(legacyJson.ToJsonString()));
        Check(true, "Legacy schema migration still rejects invalid graph references");

        Check(tree.RenameModifier(modifier.Id, "  ", out _) && modifier.Name == "" && ModifierNames.DisplayName(modifier) == "Boolean Difference",
            "Clearing a custom name restores the default type label");
        visibility.Set(sourceId, true);
        TreeStateCodec.ApplyMetadata(saved, tree, visibility);
        Check(tree.Revision == revision && ReferenceEquals(tree.Find(modifier.Id), modifier) && modifier.Name == "Main 본체" &&
              !visibility.IsVisible(sourceId), "Metadata restore changes names and visibility while retaining node instances and geometry revision");
        var before = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true));
        var wrongStructure = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, [source.Id, modifier.Id],
            [new ModifierNodeState(source.Id, source.Kind, source.ObjectId, null, []),
             new ModifierNodeState(modifier.Id, modifier.Kind, null, null, [], "Other")], true, [sourceId]);
        ExpectFormat(() => TreeStateCodec.ApplyMetadata(wrongStructure, tree, visibility));
        Check(before == TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true)) && revision == tree.Revision,
            "Metadata apply rejects a changed graph before mutating names or visibility");

        foreach (var invalid in new[] { "Bad\nName", " Bad ", new string('x', 121) })
        {
            var badName = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, state.Roots, state.Nodes.Select(node => node.Id == modifier.Id
                ? new ModifierNodeState(node.Id, node.Kind, node.ObjectId, node.ParentId, node.Children, invalid) : node), true, []);
            ExpectFormat(() => TreeStateCodec.Apply(badName, tree, visibility));
        }
        var geometryName = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, state.Roots, state.Nodes.Select(node => node.Id == source.Id
            ? new ModifierNodeState(node.Id, node.Kind, node.ObjectId, node.ParentId, node.Children, "Wrong") : node), true, []);
        ExpectFormat(() => TreeStateCodec.Encode(geometryName));
        Check(before == TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true)),
            "Saved names must be normalized, bounded and owned by Modifiers; rejected names leave state intact");
    }

    private static void Replacement()
    {
        var tree = new ModifierTreeModel();
        var objectIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in objectIds) tree.RegisterSource(id);
        var sources = objectIds.Select(id => tree.FindSource(id)!).ToArray();
        var inner = tree.AddModifier();
        var main = tree.AddModifier();
        var outer = tree.AddModifier();
        Move(tree, sources[0].Id, inner.Id, 0);
        Move(tree, sources[1].Id, inner.Id, 1);
        Move(tree, inner.Id, main.Id, 0);
        Move(tree, sources[2].Id, main.Id, 1);
        Move(tree, sources[3].Id, outer.Id, 0);
        Move(tree, main.Id, outer.Id, 1);
        var visibility = new InputVisibility();
        foreach (var id in objectIds) visibility.Set(id, true);
        var saved = TreeStateCodec.Capture(tree, visibility, true);
        var encoded = TreeStateCodec.Encode(saved);
        var revision = tree.Revision;
        foreach (var badSource in new[] { Guid.Empty, objectIds[0], objectIds[4] })
            ExpectArgument(() => tree.ReplaceSubtreeWithSource(main.Id, badSource));
        ExpectArgument(() => tree.ReplaceSubtreeWithSource(sources[0].Id, Guid.NewGuid()));
        ExpectArgument(() => tree.ReplaceSubtreeWithSource(Guid.NewGuid(), Guid.NewGuid()));
        Check(tree.Revision == revision && TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true)) == encoded,
            "Merge rejects missing or geometry targets and reused source GUIDs before mutating the tree");

        var resultId = Guid.NewGuid();
        var merged = tree.ReplaceSubtreeWithSource(main.Id, resultId);
        Check(merged.Kind == TreeNodeKind.Geometry && merged.ObjectId == resultId && merged.ParentId == outer.Id &&
              outer.Children.SequenceEqual(new[] { sources[3].Id, merged.Id }) && tree.Roots.SequenceEqual(saved.Roots) && tree.Revision == revision + 1,
            "Nested Merge replaces its Modifier in the same operand slot and invalidates geometry once");
        Check(tree.Find(main.Id) is null && tree.Find(inner.Id) is null && objectIds.Take(3).All(id => tree.FindSource(id) is null) &&
              ReferenceEquals(tree.Find(outer.Id), outer) && ReferenceEquals(tree.FindSource(objectIds[3]), sources[3]) &&
              ReferenceEquals(tree.FindSource(objectIds[4]), sources[4]) && tree.SourceCount == 3 && tree.ModifierCount == 1,
            "Merge removes the complete descendant subtree and preserves unrelated ancestors, siblings and source identities");
        var mergedState = TreeStateCodec.Capture(tree, visibility, true);
        Check(mergedState.VisibleInputIds.ToHashSet().SetEquals(objectIds.Skip(3)),
            "Merged source visibility remnants are omitted from snapshots and the new result starts hidden as an input");
        TreeStateCodec.Apply(saved, tree, visibility);
        Check(TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, true)) == encoded,
            "Restoring a pre-Merge snapshot brings back every descendant, name, order and visibility setting");

        var rootResult = tree.ReplaceSubtreeWithSource(outer.Id, Guid.NewGuid());
        Check(rootResult.ParentId is null && tree.Roots.SequenceEqual(new[] { sources[4].Id, rootResult.Id }) && tree.ModifierCount == 0,
            "Root Merge preserves its root position and removes all nested Modifiers");
        var rootBeforeBake = tree.Roots.ToArray();
        var bakedId = Guid.NewGuid();
        tree.RegisterSource(bakedId);
        Move(tree, tree.FindSource(bakedId)!.Id, null, 0);
        Check(tree.Roots.SequenceEqual(new[] { tree.FindSource(bakedId)!.Id }.Concat(rootBeforeBake)),
            "Bake can register an independent result at the top of the root list while retaining the current tree");
    }

    private static void Move(ModifierTreeModel tree, Guid id, Guid? parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }
    private static void ExpectFormat(Action action)
    {
        try { action(); }
        catch (FormatException) { return; }
        throw new Exception("FAIL: Invalid saved metadata did not throw FormatException.");
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("FAIL: Invalid replacement did not throw ArgumentException.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
