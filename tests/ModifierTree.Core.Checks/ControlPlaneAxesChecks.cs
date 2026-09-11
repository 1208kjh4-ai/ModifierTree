using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class ControlPlaneAxesChecks
{
    public static void Verify()
    {
        OwnershipAndEditing();
        PersistenceAndMigration();
        AxesAndGeometryEquality();
    }

    private static void OwnershipAndEditing()
    {
        var tree = new ModifierTreeModel();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var outer = tree.AddModifier(TreeNodeKind.BooleanUnion);
        var otherMirror = tree.AddModifier(TreeNodeKind.Mirror);
        var sourceId = Guid.NewGuid();
        tree.RegisterSource(sourceId);
        var source = tree.FindSource(sourceId)!;
        Move(tree, source.Id, mirror.Id, 0);
        Move(tree, mirror.Id, outer.Id, 0);
        var planeId = Guid.NewGuid();
        var revision = tree.Revision;
        Check(tree.SetBasePlane(mirror.Id, planeId, out _) && tree.Revision == revision + 1,
            "Set Plane adds one owned native control and invalidates geometry once");
        var plane = tree.FindSource(planeId)!;
        Check(plane.Kind == TreeNodeKind.BasePlane && plane.IsControl && !plane.IsModifier && plane.ParentId == mirror.Id &&
              plane.Children.Count == 0 && mirror.Children.SequenceEqual(new[] { plane.Id, source.Id }),
            "BasePlane appears first inside Mirror as a distinct non-Modifier leaf");
        Check(tree.SourcesInSubtree(outer.Id).SequenceEqual(new[] { planeId, sourceId }) &&
              tree.GeometrySourcesInSubtree(outer.Id).SequenceEqual(new[] { sourceId }) &&
              tree.GeometrySourcesInSubtree(plane.Id).Count == 0 && tree.GeometrySourcesInSubtree(Guid.NewGuid()).Count == 0,
            "Whole-subtree ownership includes BasePlane while geometric input enumeration excludes it at every level");
        Check(mirror.Mirror!.Union && mirror.Mirror.KeepOriginal,
            "New Mirror defaults to keeping and unioning the original with its reflection");
        revision = tree.Revision;
        Check(tree.SetBasePlane(mirror.Id, planeId, out _) && tree.Revision == revision && ReferenceEquals(plane, tree.Find(plane.Id)),
            "Assigning the same BasePlane keeps the node, ordering and geometry revision");
        Check(!tree.SetBasePlane(mirror.Id, sourceId, out _) && !tree.SetBasePlane(mirror.Id, Guid.Empty, out _) &&
              !tree.SetBasePlane(outer.Id, Guid.NewGuid(), out _) && !tree.SetBasePlane(Guid.NewGuid(), Guid.NewGuid(), out _) &&
              tree.Revision == revision,
            "Invalid or already registered BasePlane assignments do not alter source ownership");
        Check(!tree.Remove(plane.Id) && !tree.RenameModifier(plane.Id, "Wrong", out _) &&
              !tree.SetModifierEnabled(plane.Id, false, out _) && tree.Revision == revision,
            "Owned controls cannot be independently removed, renamed as Modifiers or disabled");
        Check(!tree.Move(plane.Id, null, tree.Roots.Count, out _) && !tree.Move(plane.Id, otherMirror.Id, 0, out _) &&
              !tree.Move(source.Id, plane.Id, 0, out _) && tree.Revision == revision,
            "BasePlane cannot leave its owner, enter another Mirror or contain ordinary inputs");
        Check(!tree.MoveMany(new[] { plane.Id, source.Id }, outer.Id, 1, out _) && tree.Revision == revision &&
              source.ParentId == mirror.Id,
            "A mixed drag containing an owned control rejects atomically without moving other inputs");
        Move(tree, plane.Id, mirror.Id, mirror.Children.Count);
        Check(mirror.Children.SequenceEqual(new[] { source.Id, plane.Id }) && tree.GeometrySourcesInSubtree(mirror.Id).SequenceEqual(new[] { sourceId }),
            "Reordering BasePlane within its owner does not change its control role or geometric input order");
        var replacementId = Guid.NewGuid();
        revision = tree.Revision;
        Check(tree.SetBasePlane(mirror.Id, replacementId, out _) && tree.Revision == revision + 1 && tree.FindSource(planeId) is null &&
              tree.FindSource(replacementId)!.Id == plane.Id && mirror.Children.SequenceEqual(new[] { source.Id, plane.Id }),
            "Replacing BasePlane preserves its tree identity and position while releasing only its old native registration");
        var before = Snapshot(tree);
        ExpectArgument(() => tree.CloneSubtree(plane.Id, new Dictionary<Guid, Guid> { [replacementId] = Guid.NewGuid() }));
        ExpectArgument(() => tree.CloneSubtree(mirror.Id, new Dictionary<Guid, Guid> { [sourceId] = Guid.NewGuid() }));
        Check(before == Snapshot(tree), "A control-only clone or a Mirror clone missing its control mapping changes no state");
        var mapping = tree.SourcesInSubtree(mirror.Id).ToDictionary(id => id, _ => Guid.NewGuid());
        var clone = tree.CloneSubtree(mirror.Id, mapping);
        var clonedPlane = tree.FindSource(mapping[replacementId])!;
        Check(clonedPlane.Id != plane.Id && clonedPlane.IsControl && clonedPlane.ParentId == clone.Id &&
              tree.SourcesInSubtree(clone.Id).SequenceEqual(new[] { mapping[sourceId], mapping[replacementId] }) &&
              tree.GeometrySourcesInSubtree(clone.Id).SequenceEqual(new[] { mapping[sourceId] }),
            "Duplicating Mirror creates an independent owned control and independent inputs in their saved order");
        Check(tree.Remove(mirror.Id) && tree.Find(plane.Id) is null && tree.FindSource(replacementId) is null &&
              tree.Find(source.Id)!.ParentId == outer.Id && outer.Children.SequenceEqual(new[] { source.Id, clone.Id }) &&
              tree.Find(clonedPlane.Id)?.ParentId == clone.Id,
            "Removing Mirror promotes its input and unregisters its own BasePlane without affecting another Mirror control");
        var mergedId = Guid.NewGuid();
        var merged = tree.ReplaceSubtreeWithSource(clone.Id, mergedId);
        Check(merged.ParentId == outer.Id && tree.Find(clonedPlane.Id) is null && tree.FindSource(mapping[replacementId]) is null &&
              tree.GeometrySourcesInSubtree(outer.Id).SequenceEqual(new[] { sourceId, mergedId }),
            "Merge replacement removes all subtree registrations including BasePlane and keeps the result in its original slot");
        var scopeTree = new ModifierTreeModel();
        var scopedMirror = scopeTree.AddModifier(TreeNodeKind.Mirror);
        scopeTree.SetBasePlane(scopedMirror.Id, Guid.NewGuid(), out _);
        var scopePlane = scopedMirror.Children[0];
        var scope = new TreeEditScope(scopeTree);
        Check(scope.Resolve(scopePlane) == scopedMirror.Id && scope.Enter(scopedMirror.Id) && scope.Resolve(scopePlane) == scopePlane &&
              !scope.Enter(scopePlane), "Viewport selection resolves BasePlane to its whole Mirror until entering that Mirror's edit scope");
    }

    private static void PersistenceAndMigration()
    {
        var tree = new ModifierTreeModel();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        Move(tree, array.Id, mirror.Id, 0);
        var planeObjectId = Guid.NewGuid();
        tree.SetBasePlane(mirror.Id, planeObjectId, out _);
        tree.SetArraySettings(array.Id, array.Array! with { AxisX = new ModifierVector(0, 1, 0), AxisY = new ModifierVector(1, 1, 0) }, out _);
        var json = Snapshot(tree);
        var saved = TreeStateCodec.Decode(json);
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(saved, restored, new InputVisibility());
        Check(saved.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && Snapshot(restored) == json && restored.FindSource(planeObjectId)!.IsControl &&
              restored.Find(mirror.Id)!.Mirror!.Union && restored.Find(array.Id)!.Array == array.Array,
            "Current schema preserves owned BasePlane identity, Union preference and independent nonorthogonal Array directions");
        void Reject(Action<JsonObject> mutate)
        {
            var document = JsonNode.Parse(json)!.AsObject();
            mutate(document);
            ExpectFormat(() => TreeStateCodec.Decode(document.ToJsonString()));
        }
        JsonObject Node(JsonObject document, TreeNodeKind kind) => document["nodes"]!.AsArray()
            .Single(item => item!["kind"]!.GetValue<string>() == kind.ToString())!.AsObject();
        Reject(document => Node(document, TreeNodeKind.Mirror)["mirror"]!.AsObject().Remove("union"));
        Reject(document => Node(document, TreeNodeKind.Mirror)["mirror"]!["union"] = "true");
        Reject(document => Node(document, TreeNodeKind.Array)["array"]!.AsObject().Remove("axisZ"));
        Reject(document => Node(document, TreeNodeKind.Array)["array"]!["axisX"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 });
        Reject(document => Node(document, TreeNodeKind.Array)["array"]!["axisY"]!["x"] = "NaN");
        Reject(document => Node(document, TreeNodeKind.Array)["array"]!["axisZ"]!["unexpected"] = 2);
        Check(true, "Schema 4 rejects missing, mistyped, zero and unknown Union or axis fields");
        Reject(document => Node(document, TreeNodeKind.BasePlane)["enabled"] = false);
        Reject(document => Node(document, TreeNodeKind.BasePlane)["name"] = "Control");
        Reject(document =>
        {
            var control = Node(document, TreeNodeKind.BasePlane);
            var id = control["id"]!.GetValue<string>();
            Node(document, TreeNodeKind.Mirror)["children"]!.AsArray().RemoveAt(0);
            control["parentId"] = null;
            document["roots"]!.AsArray().Add(id);
        });
        Reject(document =>
        {
            var control = Node(document, TreeNodeKind.BasePlane);
            var owner = Node(document, TreeNodeKind.Array);
            var id = control["id"]!.GetValue<string>();
            Node(document, TreeNodeKind.Mirror)["children"]!.AsArray().RemoveAt(0);
            control["parentId"] = owner["id"]!.GetValue<string>();
            owner["children"]!.AsArray().Add(id);
        });
        Reject(document =>
        {
            var control = Node(document, TreeNodeKind.BasePlane).DeepClone().AsObject();
            var id = Guid.NewGuid().ToString();
            control["id"] = id;
            control["objectId"] = Guid.NewGuid().ToString();
            document["nodes"]!.AsArray().Add(control);
            Node(document, TreeNodeKind.Mirror)["children"]!.AsArray().Add(id);
        });
        Check(true, "Saved BasePlane must remain an enabled, unnamed, unique control leaf inside its owning Mirror");

        var legacy = JsonNode.Parse(json)!.AsObject();
        legacy["schemaVersion"] = 3;
        var legacyNodes = legacy["nodes"]!.AsArray();
        foreach (var item in legacyNodes) item!.AsObject().Remove("controlBox");
        var oldControl = legacyNodes.Single(item => item!["kind"]!.GetValue<string>() == "BasePlane");
        legacyNodes.Remove(oldControl);
        var oldMirror = Node(legacy, TreeNodeKind.Mirror);
        oldMirror["children"]!.AsArray().RemoveAt(0);
        oldMirror["mirror"]!.AsObject().Remove("union");
        var oldArray = Node(legacy, TreeNodeKind.Array)["array"]!.AsObject();
        oldArray.Remove("axisX"); oldArray.Remove("axisY"); oldArray.Remove("axisZ");
        var migrated = TreeStateCodec.Decode(legacy.ToJsonString());
        var migratedMirror = migrated.Nodes.Single(node => node.Kind == TreeNodeKind.Mirror);
        var migratedArray = migrated.Nodes.Single(node => node.Kind == TreeNodeKind.Array);
        Check(migrated.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && !migratedMirror.Mirror!.Union && migratedMirror.Mirror.KeepOriginal &&
              migratedMirror.Mirror.Origin == mirror.Mirror!.Origin && migratedMirror.Mirror.Normal == mirror.Mirror.Normal &&
              migratedArray.Array!.AxisX == ArraySettings.Default.AxisX && migratedArray.Array.AxisY == ArraySettings.Default.AxisY &&
              migratedArray.Array.AxisZ == ArraySettings.Default.AxisZ,
            "Schema 3 migration preserves the historical numerical Mirror plane and separate copies and uses world Array axes");
        var migratedTree = new ModifierTreeModel();
        TreeStateCodec.Apply(migrated, migratedTree, new InputVisibility());
        Check(migratedTree.SetBasePlane(mirror.Id, Guid.NewGuid(), out _) && !migratedTree.Find(mirror.Id)!.Mirror!.Union,
            "A legacy Mirror can receive its first editable plane without silently changing its Union preference");
        var invalidLegacy = JsonNode.Parse(json)!.AsObject();
        invalidLegacy["schemaVersion"] = 3;
        ExpectFormat(() => TreeStateCodec.Decode(invalidLegacy.ToJsonString()));
        Check(true, "Older schema versions cannot silently accept new control or settings fields");
    }

    private static void AxesAndGeometryEquality()
    {
        var tree = new ModifierTreeModel();
        var array = tree.AddModifier(TreeNodeKind.Array);
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var before = TreeStateCodec.Capture(tree, new InputVisibility(), true);
        var custom = array.Array! with { AxisX = new ModifierVector(1, 1, 0), AxisY = new ModifierVector(1, 1, 0),
            AxisZ = new ModifierVector(0, 0, -1), Spacing = new ModifierVector(-3, 4, 0) };
        Check(tree.SetArraySettings(array.Id, custom, out _) && array.Array == custom,
            "Array accepts independent oblique or parallel axes, negative direction and signed or zero spacing");
        var changed = TreeStateCodec.Capture(tree, new InputVisibility(), true);
        Check(!TreeStateCodec.SameGeometry(before, changed), "Changing only Array directions invalidates the geometry snapshot");
        var revision = tree.Revision;
        foreach (var axis in new[] { ModifierVector.Zero, new ModifierVector(double.NaN, 1, 0),
                     new ModifierVector(double.PositiveInfinity, 0, 0), new ModifierVector(2e12, 0, 0), new ModifierVector(1e-13, 0, 0) })
        {
            Check(!tree.SetArraySettings(array.Id, custom with { AxisX = axis }, out _) &&
                  !tree.SetArraySettings(array.Id, custom with { AxisY = axis }, out _) &&
                  !tree.SetArraySettings(array.Id, custom with { AxisZ = axis }, out _) && array.Array == custom && tree.Revision == revision,
                "Invalid directions on any Array axis are rejected without changing valid settings or geometry revision");
        }
        Check(tree.SetArraySettings(array.Id, custom with { }, out _) && tree.Revision == revision,
            "Value-equal axis settings do not invalidate geometry or create an artificial edit");
        tree.SetMirrorSettings(mirror.Id, mirror.Mirror! with { Union = false }, out _);
        Check(!TreeStateCodec.SameGeometry(changed, TreeStateCodec.Capture(tree, new InputVisibility(), true)),
            "Changing only Mirror Union invalidates the geometry snapshot for recomputation and Undo");
    }

    private static string Snapshot(ModifierTreeModel tree) => TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), true));
    private static void Move(ModifierTreeModel tree, Guid id, Guid? parent, int index)
    {
        if (!tree.Move(id, parent, index, out var error)) throw new Exception(error);
    }
    private static void ExpectArgument(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("FAIL: Invalid control edit did not throw ArgumentException.");
    }
    private static void ExpectFormat(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new Exception("FAIL: Invalid control snapshot did not throw FormatException.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
