using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Commands;

namespace ModifierTree.Rhino.Commands;

public sealed class BakeResultCommand : Command
{
    public override string EnglishName => "MTreeBake";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => ResultCommand.Run(doc, ResultCommitMode.Bake);
}

public sealed class MergeResultCommand : Command
{
    public override string EnglishName => "MTreeMerge";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => ResultCommand.Run(doc, ResultCommitMode.Merge);
}

internal static class ResultCommand
{
    public static Result Run(RhinoDoc doc, ResultCommitMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.IsModifier != true)
        { RhinoApp.WriteLine("Select a Modifier in the Modifier Manager first."); return Result.Nothing; }
        if (!session.CommitResult(nodeId, mode, out _, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        RhinoApp.WriteLine(mode == ResultCommitMode.Bake
            ? "Result baked and registered at the top of the tree. The Modifier is unchanged."
            : "Modifier replaced with its fixed result. Original inputs are kept hidden; Undo restores the subtree.");
        return Result.Success;
    }
}
