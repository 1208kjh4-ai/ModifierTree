using System.Text.Json.Nodes;
using ModifierTree.Core;

internal static class BooleanKindsChecks
{
    public static void Verify()
    {
        var tree = new ModifierTreeModel();
        var difference = tree.AddModifier();
        var union = tree.AddModifier(TreeNodeKind.BooleanUnion);
        var intersection = tree.AddModifier(TreeNodeKind.BooleanIntersection);
        Check(union.IsModifier && intersection.IsModifier && union.Children.Count == 0 && intersection.Children.Count == 0 &&
              tree.ModifierCount == 3 && ModifierNames.DisplayName(union) == "Boolean Union" &&
              ModifierNames.DisplayName(intersection) == "Boolean Intersection",
            "Union and Intersection create empty Modifiers with their full default type names");
        tree.RenameModifier(difference.Id, "Main", out _);
        tree.RenameModifier(union.Id, "Body", out _);
        tree.RenameModifier(intersection.Id, "Common", out _);
        Check(ModifierNames.DisplayName(difference) == "(BD) Main" && ModifierNames.DisplayName(union) == "(BU) Body" &&
              ModifierNames.DisplayName(intersection) == "(BI) Common" && ModifierNames.ResultName(union) == "Body" &&
              ModifierNames.ResultName(intersection) == "Common",
            "Boolean type prefixes remain distinct and Bake or Merge result names omit those prefixes");
        if (!tree.MoveMany([difference.Id, intersection.Id], union.Id, 0, out var error)) throw new Exception(error);
        var encoded = TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), false));
        var state = TreeStateCodec.Decode(encoded);
        var restored = new ModifierTreeModel();
        TreeStateCodec.Apply(state, restored, new InputVisibility());
        Check(state.SchemaVersion == TreeStateCodec.CurrentSchemaVersion && restored.Find(union.Id)!.Kind == TreeNodeKind.BooleanUnion &&
              restored.Find(intersection.Id)!.Kind == TreeNodeKind.BooleanIntersection &&
              restored.Find(union.Id)!.Children.SequenceEqual(new[] { difference.Id, intersection.Id }) &&
              ModifierNames.DisplayName(restored.Find(intersection.Id)!) == "(BI) Common" && TreeStateCodec.Encode(state) == encoded,
            "Current schema round-trips mixed Boolean kinds, custom names, stable IDs and child order");

        var revision = tree.Revision;
        foreach (var invalid in new[] { TreeNodeKind.Geometry, (TreeNodeKind)99 })
        {
            try { tree.AddModifier(invalid); throw new Exception("FAIL: Unsupported Modifier kind was added."); }
            catch (ArgumentException) { }
        }
        var invalidJson = JsonNode.Parse(encoded)!.AsObject();
        invalidJson["nodes"]!.AsArray()[0]!["kind"] = "FutureModifier";
        try { TreeStateCodec.Decode(invalidJson.ToJsonString()); throw new Exception("FAIL: Unknown kind was decoded."); }
        catch (FormatException) { }
        Check(tree.Revision == revision && tree.ModifierCount == 3,
            "Adding supported Boolean kinds retains strict rejection of geometry and unknown future kinds");

        var invalidState = new ModifierDocumentState(TreeStateCodec.CurrentSchemaVersion, state.Roots, state.Nodes.Select(node => node.Id == intersection.Id
            ? new ModifierNodeState(node.Id, node.Kind, Guid.NewGuid(), node.ParentId, node.Children, node.Name) : node), false, []);
        try { TreeStateCodec.Apply(invalidState, tree, new InputVisibility()); throw new Exception("FAIL: Union source ID was accepted."); }
        catch (FormatException) { }
        Check(tree.Revision == revision && TreeStateCodec.Encode(TreeStateCodec.Capture(tree, new InputVisibility(), false)) == encoded,
            "Union and Intersection preserve the Modifier invariant that native source IDs belong only to geometry nodes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
