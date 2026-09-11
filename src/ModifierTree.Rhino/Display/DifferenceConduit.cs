using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Display;

internal sealed class DifferenceConduit(DocumentSession session) : DisplayConduit, IDisposable
{
    private bool CanPreview(DrawEventArgs e) =>
        e.RhinoDoc?.RuntimeSerialNumber == session.Document.RuntimeSerialNumber && session.PreviewEnabled &&
        !session.IsStateRefreshPending &&
        !session.Document.UndoActive && !session.Document.RedoActive;

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        if (!CanPreview(e)) return;
        foreach (var id in session.Tree.Roots)
            if (session.Working.IsVisible(id) && session.DisplayResult(id) is { } root)
                foreach (var result in root.Results) e.IncludeBoundingBox(result.GetBoundingBox(false));
        if (session.EditScope.ParentId.HasValue)
            foreach (var id in session.EditScope.Candidates)
                if (session.Tree.Find(id)?.IsControl != true && session.Working.IsVisible(id) && session.DisplayResult(id) is { } child)
                    foreach (var result in child.Results) e.IncludeBoundingBox(result.GetBoundingBox(false));
        foreach (var input in session.Evaluator.InputPreviews)
        {
            var obj = session.Document.Objects.FindId(input.ObjectId);
            if (obj is null || !session.CanDrawSource(input.ObjectId) || !ShowInput(input.ObjectId, obj.IsSelected(false) > 0)) continue;
            var bounds = input.Geometry.GetBoundingBox(false);
            if (obj.GetDynamicTransform(out var transform)) bounds.Transform(transform);
            e.IncludeBoundingBox(bounds);
        }
        foreach (var control in session.Tree.Nodes.Where(node => node.IsControl))
        {
            var obj = session.Document.Objects.FindId(control.ObjectId!.Value);
            if (obj is null || !session.CanDrawSource(control.ObjectId.Value) || !ShowControl(control.ObjectId.Value, obj.IsSelected(false) > 0)) continue;
            if (session.ControlCage(control.Id) is { } cage)
            {
                e.IncludeBoundingBox(cage.Bounds);
                continue;
            }
            var bounds = obj.Geometry.GetBoundingBox(false);
            if (session.ControlDisplayTransform(obj.Id) is { } transform) bounds.Transform(transform);
            e.IncludeBoundingBox(bounds);
        }
    }

    protected override void CalculateBoundingBoxZoomExtents(CalculateBoundingBoxEventArgs e) => CalculateBoundingBox(e);

    protected override void PreDrawObject(DrawObjectEventArgs e)
    {
        if (!CanPreview(e)) return;
        if (session.Working.NodeForObject(e.RhinoObject.Id) is { } nodeId)
        {
            // Root results use Rhino's native display. Live results and editing-level
            // child results are drawn by the conduit, with the same native snap geometry.
            e.DrawObject = !session.LivePreview.IsActive && !session.IsMoving && session.Tree.Roots.Contains(nodeId);
            return;
        }
        if (!session.Evaluator.InputPreviews.Any(input => input.ObjectId == e.RhinoObject.Id) &&
            session.Tree.FindSource(e.RhinoObject.Id)?.IsControl != true) return;
        // Suppress shaded originals during both normal and dynamic drawing.
        // Their selectable identity remains in Rhino; we draw just their edges below.
        e.DrawObject = false;
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if (!CanPreview(e)) return;
        {
            using var displayMode = e.Viewport.DisplayMode;
            var attributes = displayMode.DisplayAttributes;
            foreach (var id in session.Tree.Roots)
            {
                if (session.Tree.Find(id)?.IsModifier != true) continue;
                if (!session.Working.IsVisible(id)) continue;
                if (session.Working.HasObject(id) && !session.LivePreview.IsActive && !session.IsMoving) continue;
                var result = session.DisplayResult(id);
                if (result?.HasResult != true) continue;
                var primary = PrimarySource(id);
                var obj = primary is { } objectId ? session.Document.Objects.FindId(objectId) : null;
                var color = obj?.Attributes.DrawColor(session.Document, e.Viewport.Id) ?? Color.LightGray;
                using var material = PreviewAppearance.Material(attributes, obj, color, result.IsCurrent);
                var edgeColor = result.IsCurrent ? color : Color.DarkOrange;
                foreach (var brep in result.Results)
                {
                    if (attributes.ShadingEnabled) e.Display.DrawBrepShaded(brep, material);
                    if (attributes.ShowSurfaceEdges) // Edges only; avoid forcing isocurves in Rendered.
                        foreach (var edge in brep.Edges) e.Display.DrawCurve(edge, edgeColor, Math.Max(1, attributes.SurfaceEdgeThickness));
                    if (attributes.ShowIsoCurves)
                        e.Display.DrawBrepWires(brep, edgeColor, obj?.Attributes.WireDensity ?? 1);
                }
            }
        }
        DrawInputs(e, selectedOnly: false);
        DrawControls(e, selectedOnly: false);
    }

    private bool InScope(Guid objectId) => session.EditScope.ParentId is { } parent && session.Tree.FindSource(objectId)?.ParentId == parent;

    private bool WholeModifierSelected => session.Tree.Find(session.ViewportSelectedNodeId ?? Guid.Empty)?.IsModifier == true;

    private bool InputSelected(Guid objectId, bool nativeSelected) =>
        session.Tree.Find(session.SelectedNodeId ?? Guid.Empty)?.ObjectId == objectId || nativeSelected && !WholeModifierSelected;

    private bool ShowInput(Guid objectId, bool selected) => session.InputVisibility.IsVisible(objectId) || InScope(objectId) || InputSelected(objectId, selected);

    private bool ShowControl(Guid objectId, bool nativeSelected) => ShowInput(objectId, nativeSelected) || nativeSelected;

    private void DrawControls(DrawEventArgs e, bool selectedOnly)
    {
        foreach (var control in session.Tree.Nodes.Where(node => node.IsControl))
        {
            var id = control.ObjectId!.Value;
            var obj = session.Document.Objects.FindId(id);
            if (obj is null || !session.CanDrawSource(id) || obj.Geometry is not Brep brep) continue;
            var selected = obj.IsSelected(false) > 0 || session.SelectedNodeId == control.Id;
            if (selectedOnly != selected || !ShowControl(id, selected)) continue;
            if (session.ControlCage(control.Id) is { } cage)
            {
                foreach (var wire in cage.Wires) e.Display.DrawCurve(wire, selected ? Color.Yellow : Color.DeepSkyBlue, 1);
                continue;
            }
            // A damaged box retains its raw wire for repair; a valid Bend cage never
            // draws the unbent native box over its curved presentation.
            if (control.Kind == ModifierTree.Core.TreeNodeKind.ControlBox)
            {
                var transformed = session.ControlDisplayTransform(id);
                if (transformed is { } rawTransform) e.Display.PushModelTransform(rawTransform);
                try { e.Display.DrawBrepWires(brep, selected ? Color.Yellow : Color.DarkOrange, -1); }
                finally { if (transformed.HasValue) e.Display.PopModelTransform(); }
                continue;
            }
            if (session.IsMoving && session.SelectedNodeId is { } moving && session.Tree.SourcesInSubtree(moving).Contains(id)) continue;
            var dynamic = obj.GetDynamicTransform(out var transform);
            if (dynamic) e.Display.PushModelTransform(transform);
            try { e.Display.DrawBrepWires(brep, selected ? Color.Yellow : Color.DeepSkyBlue, -1); }
            finally { if (dynamic) e.Display.PopModelTransform(); }
        }
    }

    private void DrawInputs(DrawEventArgs e, bool selectedOnly)
    {
        foreach (var input in session.Evaluator.InputPreviews)
        {
            var obj = session.Document.Objects.FindId(input.ObjectId);
            if (obj is null || !session.CanDrawSource(input.ObjectId)) continue;
            var selected = InputSelected(input.ObjectId, obj.IsSelected(false) > 0);
            if (selectedOnly != selected || !ShowInput(input.ObjectId, selected)) continue;
            // MTreeMove provides its own translated edge feedback; avoid a stationary duplicate.
            if (session.IsMoving && session.SelectedNodeId is { } moving && session.Tree.SourcesInSubtree(moving).Contains(input.ObjectId)) continue;
            var color = selected ? Color.Yellow : input.IsCutter ? Color.DarkOrange : Color.SlateGray;
            var dynamic = obj.GetDynamicTransform(out var transform);
            if (dynamic) e.Display.PushModelTransform(transform);
            try { e.Display.DrawBrepWires(input.Geometry, color, -1); }
            finally { if (dynamic) e.Display.PopModelTransform(); }
        }
    }

    private void DrawScopeAndSelection(DrawEventArgs e)
    {
        if (session.EditScope.ParentId.HasValue)
            foreach (var id in session.EditScope.Candidates)
            {
                var node = session.Tree.Find(id)!;
                if (node.IsModifier && !session.Working.IsVisible(id)) continue;
                if (node.IsControl) continue; // DrawControls also applies native dynamic transforms.
                // Selected sources are already drawn with native dynamic transforms.
                if (node.ObjectId is { } objectId && InputSelected(objectId, session.Document.Objects.FindId(objectId)?.IsSelected(false) > 0)) continue;
                if (node.ObjectId is { } source && !session.CanDrawSource(source)) continue;
                if (session.DisplayResult(id) is not { HasResult: true } result) continue;
                foreach (var brep in result.Results) e.Display.DrawBrepWires(brep, Color.DeepSkyBlue, -1);
            }
        if (session.ViewportSelectedNodeId is { } selected && session.Tree.Find(selected)?.IsModifier == true &&
            session.Working.IsVisible(selected) &&
            session.DisplayResult(selected) is { HasResult: true } selection && !session.IsMoving)
            foreach (var brep in selection.Results) e.Display.DrawBrepWires(brep, Color.Yellow, -1);
    }

    private Guid? PrimarySource(Guid nodeId)
    {
        var sources = session.Tree.GeometrySourcesInSubtree(nodeId);
        return sources.Count > 0 ? sources[0] : null;
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
        if (CanPreview(e)) { DrawScopeAndSelection(e); DrawInputs(e, selectedOnly: true); DrawControls(e, selectedOnly: true); }
        if (CanPreview(e) && session.Tree.Roots.Any(id => session.DisplayResult(id) is { HasResult: true, IsCurrent: false }))
            e.Display.Draw2dText("An input failed: orange shows the last valid result", Color.DarkOrange, new Point2d(15, 35), false, 16);
    }

    // Rhino's fast GetPoint/Gumball feedback can redraw only this overlay channel.
    protected override void DrawOverlay(DrawEventArgs e)
    {
        if (CanPreview(e)) { DrawScopeAndSelection(e); DrawInputs(e, selectedOnly: true); DrawControls(e, selectedOnly: true); }
    }

    public void Dispose() => Enabled = false;
}
