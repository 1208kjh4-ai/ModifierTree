using System.Drawing;
using ModifierTree.Core;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ModifierTree.Rhino.Commands;

public sealed class SetMirrorPlaneCommand : Command
{
    public override string EnglishName => "MTreeSetPlane";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (!session.CanEditTree || session.SelectedNodeId is not { } nodeId || session.Tree.Find(nodeId)?.Kind != TreeNodeKind.Mirror)
        { RhinoApp.WriteLine("Select a Mirror in the Modifier Manager first."); return Result.Nothing; }

        using var originPick = new GetPoint();
        originPick.SetCommandPrompt("Mirror plane origin");
        if (originPick.Get() != GetResult.Point) return originPick.CommandResult();
        var origin = originPick.Point();

        using var xPick = new GetPoint();
        xPick.SetCommandPrompt("Point on mirror plane X axis");
        xPick.SetBasePoint(origin, true);
        xPick.DrawLineFromPoint(origin, true);
        if (xPick.Get() != GetResult.Point) return xPick.CommandResult();
        var xPoint = xPick.Point();
        var xDirection = xPoint - origin;
        if (xDirection.Length <= doc.ModelAbsoluteTolerance || !xDirection.Unitize())
        { RhinoApp.WriteLine("The second point must be distinct from the plane origin. The plane was not changed."); return Result.Failure; }

        using var thirdPick = new GetPoint();
        thirdPick.SetCommandPrompt("Third point on mirror plane");
        thirdPick.SetBasePoint(origin, true);
        thirdPick.FullFrameRedrawDuringGet = true;
        thirdPick.DynamicDraw += (_, e) =>
        {
            e.Display.DrawLine(origin, xPoint, Color.DeepSkyBlue, 2);
            var preview = new Plane(origin, xPoint, e.CurrentPoint);
            if (!preview.IsValid) return;
            var width = origin.DistanceTo(xPoint);
            var height = Math.Abs((e.CurrentPoint - origin) * preview.YAxis);
            if (height <= doc.ModelAbsoluteTolerance) return;
            var right = origin + preview.XAxis * width;
            var top = origin + preview.YAxis * height;
            e.Display.DrawPolyline(new[] { origin, right, right + preview.YAxis * height, top, origin }, Color.DeepSkyBlue, 2);
            e.Display.DrawArrow(new Line(origin, origin + preview.ZAxis * (Math.Max(width, height) * 0.25)), Color.DeepSkyBlue);
        };
        if (thirdPick.Get() != GetResult.Point) return thirdPick.CommandResult();
        var third = thirdPick.Point();
        var perpendicular = third - origin - xDirection * ((third - origin) * xDirection);
        var plane = new Plane(origin, xPoint, third);
        if (perpendicular.Length <= doc.ModelAbsoluteTolerance || !plane.IsValid)
        { RhinoApp.WriteLine("The three plane points must not be collinear. The plane was not changed."); return Result.Failure; }
        if (!session.SetMirrorPlane(nodeId, plane, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        return Result.Success;
    }
}
