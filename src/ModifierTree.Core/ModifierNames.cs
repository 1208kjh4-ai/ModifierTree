namespace ModifierTree.Core;

/// <summary>Keeps generated type prefixes separate from names that users edit or give baked objects.</summary>
public static class ModifierNames
{
    public const int MaxNameLength = 120;

    public static string TypeName(TreeNodeKind kind) => kind switch
    {
        TreeNodeKind.BooleanDifference => "Boolean Difference",
        TreeNodeKind.BooleanUnion => "Boolean Union",
        TreeNodeKind.BooleanIntersection => "Boolean Intersection",
        TreeNodeKind.Mirror => "Mirror",
        TreeNodeKind.Array => "Array",
        TreeNodeKind.Bend => "Bend",
        _ => throw new ArgumentException("Unsupported Modifier kind.", nameof(kind))
    };

    public static string Abbreviation(TreeNodeKind kind) => kind switch
    {
        TreeNodeKind.BooleanDifference => "BD",
        TreeNodeKind.BooleanUnion => "BU",
        TreeNodeKind.BooleanIntersection => "BI",
        TreeNodeKind.Mirror => "Mirror",
        TreeNodeKind.Array => "Array",
        TreeNodeKind.Bend => "Bend",
        _ => throw new ArgumentException("Unsupported Modifier kind.", nameof(kind))
    };

    public static string DisplayName(ModifierTreeNode node) => node.Name.Length == 0
        ? TypeName(node.Kind)
        : $"({Abbreviation(node.Kind)}) {node.Name}";

    public static string ResultName(ModifierTreeNode node) => node.Name.Length == 0 ? TypeName(node.Kind) : node.Name;

    public static bool TryNormalize(string? name, out string normalized, out string error)
    {
        normalized = (name ?? "").Trim();
        error = "";
        if (name is not null && name.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))
        {
            error = "A Modifier name cannot contain line breaks or control characters.";
            return false;
        }
        if (normalized.Length > MaxNameLength)
        {
            error = $"A Modifier name can contain at most {MaxNameLength} characters.";
            return false;
        }
        return true;
    }
}
