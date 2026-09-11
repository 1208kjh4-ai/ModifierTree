using System.Drawing;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ModifierTree.Rhino.Commands;

public sealed class MoveTreeCommand : Command
{
    public override string EnglishName => "MTreeMove";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        if (session.SelectedNodeId is not { } nodeId)
        { RhinoApp.WriteLine("Select an item in the Modifier Manager first."); return Result.Nothing; }
        if (!session.ValidateMove(nodeId, out var error))
        { RhinoApp.WriteLine(error); return Result.Failure; }
        session.IsMoving = true;
        try
        {
            using var start = new GetPoint();
            start.SetCommandPrompt("Point to move Modifier Tree item from");
            if (start.Get() != GetResult.Point) return start.CommandResult();
            var origin = start.Point();
            using var end = new GetPoint();
            end.FullFrameRedrawDuringGet = true;
            end.SetCommandPrompt("Point to move Modifier Tree item to");
            end.SetBasePoint(origin, true);
            end.DrawLineFromPoint(origin, true);
            end.MouseMove += (_, e) => session.PreviewMove(nodeId, Transform.Translation(e.Point - origin));
            end.DynamicDraw += (_, e) =>
            {
                // The conduit draws the curved cage from PreviewMove's absolute
                // transform, including its new scale/frame. Do not overlay a raw box.
                if (session.PreviewEnabled && session.Tree.Find(nodeId)?.Kind == ModifierTree.Core.TreeNodeKind.ControlBox) return;
                e.Display.PushModelTransform(Transform.Translation(e.CurrentPoint - origin));
                try
                {
                    var result = session.Evaluator.Find(nodeId);
                    if (result?.IsCurrent == true && result.Results.Count > 0)
                        foreach (var brep in result.Results) e.Display.DrawBrepWires(brep, Color.Yellow, -1);
                    else
                        foreach (var id in session.Tree.SourcesInSubtree(nodeId))
                        {
                            if (session.PreviewEnabled && session.Tree.FindSource(id)?.Kind == ModifierTree.Core.TreeNodeKind.ControlBox) continue;
                            var obj = doc.Objects.FindId(id);
                            switch (obj?.Geometry)
                            {
                                case Brep brep: e.Display.DrawBrepWires(brep, Color.Yellow, -1); break;
                                case Extrusion extrusion: e.Display.DrawExtrusionWires(extrusion, Color.Yellow); break;
                                case Curve curve: e.Display.DrawCurve(curve, Color.Yellow); break;
                            }
                        }
                }
                finally { e.Display.PopModelTransform(); }
            };
            if (end.Get() != GetResult.Point) return end.CommandResult();
            if (!session.MoveTreeNode(nodeId, Transform.Translation(end.Point() - origin), out error))
            { RhinoApp.WriteLine(error); return Result.Failure; }
            session.RequestRebuild();
            return Result.Success;
        }
        finally
        {
            session.IsMoving = false;
            session.ClearLivePreview();
            session.RequestRefresh();
            doc.Views.Redraw();
        }
    }
}
