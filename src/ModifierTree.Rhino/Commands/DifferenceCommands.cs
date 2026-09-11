using ModifierTree.Rhino.UI;
using ModifierTree.Core;
using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace ModifierTree.Rhino.Commands;

public sealed class DifferenceCommand : Command
{
    public override string EnglishName => "MTreeDifference";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (ModifierTreePlugIn.Instance.Session(doc).AddModifier() == Guid.Empty) return Result.Failure;
        Panels.OpenPanel(typeof(ModifierManagerPanel));
        RhinoApp.WriteLine("Empty Boolean Difference added. Drag inputs into it in the Modifier Manager.");
        return Result.Success;
    }
}

public sealed class UnionCommand : Command
{
    public override string EnglishName => "MTreeUnion";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => AddBooleanModifier.Run(doc, TreeNodeKind.BooleanUnion);
}

public sealed class IntersectionCommand : Command
{
    public override string EnglishName => "MTreeIntersection";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => AddBooleanModifier.Run(doc, TreeNodeKind.BooleanIntersection);
}

internal static class AddBooleanModifier
{
    public static Result Run(RhinoDoc doc, TreeNodeKind kind)
    {
        if (ModifierTreePlugIn.Instance.Session(doc).AddModifier(kind) == Guid.Empty) return Result.Failure;
        Panels.OpenPanel(typeof(ModifierManagerPanel));
        RhinoApp.WriteLine($"Empty {ModifierNames.TypeName(kind)} added. Drag inputs into it in the Modifier Manager.");
        return Result.Success;
    }
}

public sealed class RebuildDifferenceCommand : Command
{
    public override string EnglishName => "MTreeRebuild";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        ModifierTreePlugIn.Instance.Session(doc).RequestRebuild();
        return Result.Success;
    }
}
