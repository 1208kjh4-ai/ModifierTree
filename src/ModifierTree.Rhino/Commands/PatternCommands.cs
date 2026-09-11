using ModifierTree.Core;
using Rhino;
using Rhino.Commands;

namespace ModifierTree.Rhino.Commands;

public sealed class MirrorModifierCommand : Command
{
    public override string EnglishName => "MTreeMirror";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => AddBooleanModifier.Run(doc, TreeNodeKind.Mirror);
}

public sealed class ArrayModifierCommand : Command
{
    public override string EnglishName => "MTreeArray";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => AddBooleanModifier.Run(doc, TreeNodeKind.Array);
}

public sealed class DuplicateModifierCommand : Command
{
    public override string EnglishName => "MTreeDuplicate";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.IsModifier != true)
        { RhinoApp.WriteLine("Select a Modifier in the Modifier Manager first."); return Result.Nothing; }
        if (!session.DuplicateModifier(nodeId, out _, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        RhinoApp.WriteLine("Modifier subtree duplicated with independent source objects. One Undo restores the original tree.");
        return Result.Success;
    }
}
