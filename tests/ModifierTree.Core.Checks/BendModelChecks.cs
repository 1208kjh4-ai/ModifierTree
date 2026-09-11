using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class BendModelChecks
{
    public static void Verify()
    {
        OwnershipAndOrder();
        SettingsAndHistory();
        PersistenceAndMigration();
        DepthLimit();
    }

    private static void OwnershipAndOrder()
    {
        var tree = new ModifierTreeModel();
        var outer = tree.AddModifier(TreeNodeKind.BooleanUnion);
        var bend = tree.AddModifier(TreeNodeKind.Bend);
        var secondBend = tree.AddModifier(TreeNodeKind.Bend);
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var geometryId = Guid.NewGuid();
        tree.RegisterSource(geometryId);
        var geometry = tree.FindSource(geometryId)!;
        Move(tree, geometry.Id, bend.Id, 0);
        Move(tree, bend.Id, outer.Id, 0);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var revision = tree.Revision;
        Check(tree.AddControlBox(bend.Id, firstId, out _) && tree.AddControlBox(bend.Id, secondId, out _) && tree.Revision == revision + 2,
            "Bend accepts multiple independently owned controls and invalidates once per addition");
        var first = tree.FindSource(firstId)!;
        var second = tree.FindSource(secondId)!;
        Check(first.Kind == TreeNodeKind.ControlBox && first.IsControl && !first.IsModifier && first.ControlBox == ControlBoxSettings.Default &&
              first.ParentId == bend.Id && bend.Children.SequenceEqual(new[] { first.Id, second.Id, geometry.Id }),
            "New Control Boxes retain insertion order before inputs and have independent default settings");
        Check(tree.SourcesInSubtree(outer.Id).SequenceEqual(new[] { firstId, secondId, geometryId }) &&
              tree.GeometrySourcesInSubtree(outer.Id).SequenceEqual(new[] { geometryId }) && tree.GeometrySourcesInSubtree(first.Id).Count == 0,
            "Bend transform and duplication ownership includes controls while geometric output sources exclude them");
        revision = tree.Revision;
        Check(!tree.AddControlBox(mirror.Id, Guid.NewGuid(), out _) && !tree.AddControlBox(Guid.NewGuid(), Guid.NewGuid(), out _) &&
              !tree.AddControlBox(bend.Id, Guid.Empty, out _) && !tree.AddControlBox(bend.Id, geometryId, out _) &&
              !tree.AddControlBox(secondBend.Id, firstId, out _) && tree.Revision == revision,
            "Control Box registration rejects foreign owners, empty IDs and already registered objects without changes");
        var before = Snapshot(tree);
        Check(!tree.Move(first.Id, null, tree.Roots.Count, out _) && !tree.Move(first.Id, mirror.Id, 0, out _) &&
              !tree.Move(first.Id, outer.Id, 0, out _) && !tree.Move(geometry.Id, first.Id, 0, out _) && Snapshot(tree) == before,
            "Control Boxes cannot leave Bend ownership or become containers");
        Check(!tree.MoveMany(new[] { first.Id, geometry.Id }, mirror.Id, 0, out _) && Snapshot(tree) == before,
            "A rejected mixed control and input drag leaves every source in place");
        Move(tree, first.Id, bend.Id, bend.Children.Count);
        Check(bend.Children.SequenceEqual(new[] { second.Id, geometry.Id, first.Id }) &&
              tree.GeometrySourcesInSubtree(bend.Id).SequenceEqual(new[] { geometryId }),
            "Controls can be interleaved with geometry while preserving a distinct evaluation order");
        tree.SetControlBoxSettings(first.Id, new ControlBoxSettings(-75, false), out _);
        Move(tree, first.Id, secondBend.Id, 0);
        Check(first.ParentId == secondBend.Id && first.ControlBox == new ControlBoxSettings(-75, false) &&
              secondBend.Children.SequenceEqual(new[] { first.Id }) && bend.Children.SequenceEqual(new[] { second.Id, geometry.Id }),
            "Moving a Control Box to another Bend preserves its ID, object and settings");
        revision = tree.Revision;
        Check(!tree.SetModifierEnabled(first.Id, false, out _) && !tree.RenameModifier(first.Id, "Wrong", out _) && tree.Revision == revision,
            "Control Box names belong to Rhino and only the Bend can be enabled or disabled");
        tree.SetControlBoxSettings(second.Id, new ControlBoxSettings(110, false), out _);
        var map = tree.SourcesInSubtree(bend.Id).ToDictionary(id => id, _ => Guid.NewGuid());
        var clone = tree.CloneSubtree(bend.Id, map);
        var clonedControl = tree.FindSource(map[secondId])!;
        Check(clonedControl.Id != second.Id && clonedControl.ParentId == clone.Id && clonedControl.ControlBox == second.ControlBox &&
              tree.SourcesInSubtree(clone.Id).SequenceEqual(new[] { map[secondId], map[geometryId] }),
            "Bend subtree duplication remaps every control and geometry source in the original order");
        tree.SetControlBoxSettings(clonedControl.Id, new ControlBoxSettings(15), out _);
        Check(second.ControlBox == new ControlBoxSettings(110, false) && clonedControl.ControlBox == new ControlBoxSettings(15),
            "Editing a duplicated Control Box leaves the original box's deformation settings unchanged");
        var missingMap = new Dictionary<Guid, Guid> { [geometryId] = Guid.NewGuid() };
        before = Snapshot(tree);
        ExpectArgument(() => tree.CloneSubtree(bend.Id, missingMap));
        Check(Snapshot(tree) == before, "A Bend clone missing a control mapping is rejected atomically");
        var scope = new TreeEditScope(tree);
        Check(scope.Resolve(first.Id) == secondBend.Id && scope.Enter(secondBend.Id) && scope.Resolve(first.Id) == first.Id && !scope.Enter(first.Id),
            "A Control Box selects its whole Bend until entering the Bend edit scope");
        Check(tree.Remove(first.Id) && tree.FindSource(firstId) is null && secondBend.Children.Count == 0,
            "An individual Control Box can be removed without removing its Bend or siblings");
        Check(tree.Remove(bend.Id) && tree.FindSource(secondId) is null && tree.Find(geometry.Id)?.ParentId == outer.Id &&
              outer.Children.SequenceEqual(new[] { geometry.Id, clone.Id }) && tree.Find(clonedControl.Id) is not null,
            "Removing Bend releases its controls and promotes only geometry children without changing other controls");
        var mergedId = Guid.NewGuid();
        var merged = tree.ReplaceSubtreeWithSource(clone.Id, mergedId);
        Check(merged.ParentId == outer.Id && tree.FindSource(map[secondId]) is null && tree.FindSource(map[geometryId]) is null &&
              outer.Children.SequenceEqual(new[] { geometry.Id, merged.Id }),
            "Merge replaces the Bend subtree in place and releases control registrations along with original inputs");
        Check((int)TreeNodeKind.Geometry == 0 && (int)TreeNodeKind.BasePlane == 6 && (int)TreeNodeKind.Bend == 7 &&
              (int)TreeNodeKind.ControlBox == 8 && ModifierNames.DisplayName(secondBend) == "Bend" &&
              tree.RenameModifier(secondBend.Id, "Main", out _) && ModifierNames.DisplayName(secondBend) == "(Bend) Main",
            "Bend adds new enum values without renumbering existing kinds and uses its readable type prefix");
    }

    private static void SettingsAndHistory()
    {
        var tree = new ModifierTreeModel();
        var bend = tree.AddModifier(TreeNodeKind.Bend);
        var objectId = Guid.NewGuid();
        tree.AddControlBox(bend.Id, objectId, out _);
        var box = tree.FindSource(objectId)!;
        var visibility = new InputVisibility();
        var original = TreeStateCodec.Capture(tree, visibility, true);
        var revision = tree.Revision;
        Check(tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(-180, false), out _) && tree.Revision == revision + 1 &&
              !TreeStateCodec.SameGeometry(original, TreeStateCodec.Capture(tree, visibility, true)),
            "Changing Bend Strength and mode is one geometry edit for recomputation and Undo");
        var changed = TreeStateCodec.Capture(tree, visibility, true);
        tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(-180, true), out _);
        Check(!TreeStateCodec.SameGeometry(changed, TreeStateCodec.Capture(tree, visibility, true)),
            "Changing only Limited mode invalidates geometry equality");
        revision = tree.Revision;
        foreach (var strength in new[] { -180.001, 180.001, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            if (tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(strength), out _)) throw new Exception("Accepted invalid Bend Strength.");
        Check(!tree.SetControlBoxSettings(bend.Id, new ControlBoxSettings(), out _) &&
              !tree.SetControlBoxSettings(Guid.NewGuid(), new ControlBoxSettings(), out _) &&
              !ModifierSettings.TryValidate((ControlBoxSettings?)null, out _) && tree.Revision == revision,
            "Nonfinite, out-of-range or misdirected Control Box edits leave the previous settings and revision intact");
        Check(tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(-180, true), out _) && tree.Revision == revision,
            "Reapplying equal Control Box settings causes no artificial geometry edit");
        Check(tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(180), out _) &&
              tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(-0.0), out _) &&
              BitConverter.DoubleToInt64Bits(box.ControlBox!.Strength) == 0,
            "Strength accepts both direction limits and normalizes negative zero to an identity value");
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(original, restored, visibility);
        ExpectFormat(() => TreeStateCodec.ApplyMetadata(changed, restored, visibility));
        Check(restored.Find(box.Id)!.ControlBox == ControlBoxSettings.Default,
            "Metadata-only application cannot overwrite a Control Box geometry setting");
        TreeStateCodec.Apply(changed, restored, visibility);
        Check(restored.Find(box.Id)!.ControlBox == new ControlBoxSettings(-180, false) && restored.FindSource(objectId)!.ParentId == bend.Id,
            "Full Undo or Redo restoration preserves Control Box settings, identity and ownership");
        TreeStateCodec.Apply(original, restored, visibility);
        Check(restored.Find(box.Id)!.ControlBox == ControlBoxSettings.Default,
            "Restoring the earlier Bend snapshot restores the earlier per-box deformation");
    }

    private static void PersistenceAndMigration()
    {
        var tree = new ModifierTreeModel();
        var bend = tree.AddModifier(TreeNodeKind.Bend);
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        tree.AddControlBox(bend.Id, Guid.NewGuid(), out _);
        tree.AddControlBox(bend.Id, Guid.NewGuid(), out _);
        var boxes = bend.Children.Select(id => tree.Find(id)!).ToArray();
        tree.SetControlBoxSettings(boxes[0].Id, new ControlBoxSettings(-35, false), out _);
        tree.SetBasePlane(mirror.Id, Guid.NewGuid(), out _);
        var visibility = new InputVisibility();
        visibility.Set(boxes[0].ObjectId!.Value, true);
        var json = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, visibility, false));
        var saved = TreeStateCodec.Decode(json);
        var restored = new ModifierTreeModel();
        var restoredVisibility = new InputVisibility();
        TreeStateCodec.Apply(saved, restored, restoredVisibility);
        Check(saved.SchemaVersion == 5 && TreeStateCodec.Encode(TreeStateCodec.Capture(restored, restoredVisibility, false)) == json &&
              restored.Find(boxes[0].Id)!.ControlBox == new ControlBoxSettings(-35, false) && !saved.PreviewEnabled,
            "Schema 5 roundtrips multiple controls, their settings, tree order and visibility deterministically");
        void Reject(Action<JsonObject> mutation)
        {
            var document = JsonNode.Parse(json)!.AsObject();
            mutation(document);
            ExpectFormat(() => TreeStateCodec.Decode(document.ToJsonString()));
        }
        JsonObject Node(JsonObject document, Guid id) => document["nodes"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<Guid>() == id)!.AsObject();
        Reject(document => Node(document, boxes[0].Id).Remove("controlBox"));
        Reject(document => Node(document, boxes[0].Id)["controlBox"] = null);
        Reject(document => Node(document, bend.Id)["controlBox"] = new JsonObject { ["strength"] = 45, ["limited"] = true });
        Reject(document => Node(document, boxes[0].Id)["array"] = Node(document, array.Id)["array"]!.DeepClone());
        Reject(document => Node(document, boxes[0].Id)["controlBox"]!["strength"] = 181);
        Reject(document => Node(document, boxes[0].Id)["controlBox"]!["strength"] = "NaN");
        Reject(document => Node(document, boxes[0].Id)["controlBox"]!["limited"] = "true");
        Reject(document => Node(document, boxes[0].Id)["controlBox"]!.AsObject().Remove("limited"));
        Reject(document => Node(document, boxes[0].Id)["controlBox"]!["keepYLength"] = false);
        Check(true, "Schema 5 rejects missing, extra, mistyped, invalid or foreign Control Box settings");
        Reject(document => Node(document, boxes[0].Id)["enabled"] = false);
        Reject(document => Node(document, boxes[0].Id)["name"] = "Wrong");
        Reject(document => Node(document, boxes[0].Id)["objectId"] = Node(document, boxes[1].Id)["objectId"]!.DeepClone());
        Reject(document =>
        {
            Node(document, bend.Id)["children"]!.AsArray().RemoveAt(0);
            Node(document, boxes[0].Id)["parentId"] = null;
            document["roots"]!.AsArray().Add(boxes[0].Id);
        });
        Reject(document =>
        {
            Node(document, bend.Id)["children"]!.AsArray().RemoveAt(0);
            Node(document, boxes[0].Id)["parentId"] = mirror.Id;
            Node(document, mirror.Id)["children"]!.AsArray().Add(boxes[0].Id);
        });
        Check(true, "A saved Control Box must be an enabled unnamed source leaf with unique object identity owned by Bend");
        var malformed = json.Replace("\"strength\":-35", "\"strength\":-35,\"strength\":-35", StringComparison.Ordinal);
        ExpectFormat(() => TreeStateCodec.Decode(malformed));
        Check(true, "Duplicate Control Box JSON fields are rejected");

        var legacyTree = new ModifierTreeModel();
        var oldMirror = legacyTree.AddModifier(TreeNodeKind.Mirror);
        var oldArray = legacyTree.AddModifier(TreeNodeKind.Array);
        legacyTree.SetBasePlane(oldMirror.Id, Guid.NewGuid(), out _);
        legacyTree.SetArraySettings(oldArray.Id, oldArray.Array! with { AxisX = new ModifierVector(1, 1, 0) }, out _);
        var current = JsonNode.Parse(Snapshot(legacyTree))!.AsObject();
        current["schemaVersion"] = 4;
        foreach (var node in current["nodes"]!.AsArray()) node!.AsObject().Remove("controlBox");
        var migrated = TreeStateCodec.Decode(current.ToJsonString());
        Check(migrated.SchemaVersion == 5 && migrated.Nodes.All(node => node.ControlBox is null) &&
              migrated.Nodes.Single(node => node.Id == oldMirror.Id).Mirror == oldMirror.Mirror &&
              migrated.Nodes.Single(node => node.Id == oldArray.Id).Array == oldArray.Array &&
              migrated.Nodes.Single(node => node.Kind == TreeNodeKind.BasePlane).ParentId == oldMirror.Id,
            "Schema 4 migrates without changing Mirror, BasePlane or custom Array axes");
        var invalidLegacy = JsonNode.Parse(json)!.AsObject();
        invalidLegacy["schemaVersion"] = 4;
        foreach (var node in invalidLegacy["nodes"]!.AsArray()) node!.AsObject().Remove("controlBox");
        ExpectFormat(() => TreeStateCodec.Decode(invalidLegacy.ToJsonString()));
        Check(true, "Schema 4 cannot accept Bend or Control Box kinds introduced by schema 5");
    }

    private static void DepthLimit()
    {
        var ids = Enumerable.Range(0, TreeStateCodec.MaxTreeDepth).Select(_ => Guid.NewGuid()).ToArray();
        var nodes = ids.Select((id, index) => new ModifierNodeState(id, TreeNodeKind.Bend, null,
            index == 0 ? null : ids[index - 1], index + 1 < ids.Length ? new[] { ids[index + 1] } : []));
        var tree = new ModifierTreeModel();
        TreeStateCodec.Apply(new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, [ids[0]], nodes, true, []),
            tree, new InputVisibility());
        var before = Snapshot(tree);
        var objectId = Guid.NewGuid();
        Check(!tree.AddControlBox(ids[^1], objectId, out _) && tree.FindSource(objectId) is null && Snapshot(tree) == before,
            "Adding a box beyond the persisted depth limit rejects without registering an unusable tree");
        Check(tree.AddControlBox(ids[^2], objectId, out _) && tree.FindSource(objectId)?.ParentId == ids[^2],
            "A Control Box at the maximum allowed tree depth remains saveable");
        _ = Snapshot(tree);
    }

    private static string Snapshot(ModifierTreeModel tree) => TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), true));
    private static void Move(ModifierTreeModel tree, Guid id, Guid? parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("FAIL: Invalid Bend operation did not throw ArgumentException.");
    }
    private static void ExpectFormat(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new Exception("FAIL: Invalid Bend snapshot did not throw FormatException.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
