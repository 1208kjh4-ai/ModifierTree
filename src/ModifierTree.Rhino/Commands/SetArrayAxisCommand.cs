using System.Drawing;
using ModifierTree.Core;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ModifierTree.Rhino.Commands;

public sealed class SetArrayAxisCommand : Command
{
    public override string EnglishName => "MTreeSetAxis";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (!session.CanEditTree || session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.Kind != TreeNodeKind.Array)
        { RhinoApp.WriteLine("Select an Array in the Modifier Manager first."); return Result.Nothing; }
        using var axisPick = new GetOption();
        axisPick.SetCommandPrompt("Array axis to set");
        var options = new[] { axisPick.AddOption("X"), axisPick.AddOption("Y"), axisPick.AddOption("Z") };
        if (axisPick.Get() != GetResult.Option) return axisPick.CommandResult();
        var axisIndex = System.Array.IndexOf(options, axisPick.OptionIndex());
        if (axisIndex < 0) return Result.Cancel;

        using var startPick = new GetPoint();
        startPick.SetCommandPrompt($"Start of Array {"XYZ"[axisIndex]} direction");
        if (startPick.Get() != GetResult.Point) return startPick.CommandResult();
        var start = startPick.Point();
        using var endPick = new GetPoint();
        endPick.SetCommandPrompt($"End of Array {"XYZ"[axisIndex]} direction (spacing stays unchanged)");
        endPick.SetBasePoint(start, true);
        endPick.FullFrameRedrawDuringGet = true;
        endPick.DynamicDraw += (_, e) =>
        {
            var arrow = new Line(start, e.CurrentPoint);
            if (arrow.IsValid && arrow.Length > RhinoMath.ZeroTolerance) e.Display.DrawArrow(arrow, Color.DeepSkyBlue);
        };
        if (endPick.Get() != GetResult.Point) return endPick.CommandResult();
        var direction = endPick.Point() - start;
        if (direction.Length <= doc.ModelAbsoluteTolerance || !direction.Unitize())
        { RhinoApp.WriteLine("Pick two distinct points to set an Array axis. The direction was not changed."); return Result.Failure; }
        if (!session.SetArrayAxis(nodeId, axisIndex, direction, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        return Result.Success;
    }
}
