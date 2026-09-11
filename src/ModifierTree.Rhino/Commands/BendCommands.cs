using ModifierTree.Core;
using Rhino;
using Rhino.Commands;

namespace ModifierTree.Rhino.Commands;

public sealed class BendModifierCommand : Command
{
    public override string EnglishName => "MTreeBend";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode) => AddBooleanModifier.Run(doc, TreeNodeKind.Bend);
}

public sealed class AddControlBoxCommand : Command
{
    public override string EnglishName => "MTreeAddControlBox";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (!session.CanEditTree || session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.Kind != TreeNodeKind.Bend)
        { RhinoApp.WriteLine("Select a Bend in the Modifier Manager first."); return Result.Nothing; }
        if (!session.AddControlBox(nodeId, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        RhinoApp.WriteLine("Control Box added. Use the Gumball to move, rotate or scale it; edit Strength in the Modifier Manager.");
        return Result.Success;
    }
}

public sealed class FitControlBoxCommand : Command
{
    public override string EnglishName => "MTreeFitControlBox";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (!session.CanEditTree || session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.Kind != TreeNodeKind.ControlBox)
        { RhinoApp.WriteLine("Select a Control Box in the Modifier Manager first."); return Result.Nothing; }
        if (!session.FitControlBox(nodeId, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        RhinoApp.WriteLine("Control Box fitted to the Bend inputs with its orientation preserved.");
        return Result.Success;
    }
}
