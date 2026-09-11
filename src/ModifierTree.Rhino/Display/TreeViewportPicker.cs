using Point = System.Drawing.Point;
using ModifierTree.Rhino.Modifiers;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace ModifierTree.Rhino.Display;

internal static class TreeViewportPicker
{
    private static PickContext Context(RhinoView view, Point point)
    {
        using var mode = view.ActiveViewport.DisplayMode;
        var pick = new PickContext
        {
            View = view, PickStyle = PickStyle.PointPick, PickGroupsEnabled = false,
            PickMode = mode.DisplayAttributes.ShadingEnabled ? PickMode.Shaded : PickMode.Wireframe
        };
        pick.SetPickTransform(view.ActiveViewport.GetPickTransform(point.X, point.Y));
        if (view.ActiveViewport.GetFrustumLine(point.X, point.Y, out var line)) pick.PickLine = line;
        pick.UpdateClippingPlanes();
        return pick;
    }

    private static bool Suppressed(DocumentSession session, Guid objectId) =>
        session.Working.IsProxy(objectId) || session.Tree.FindSource(objectId)?.IsControl == true || session.Evaluator.InputPreviews.Any(input => input.ObjectId == objectId);

    private static bool VisibleControl(DocumentSession session, Guid nodeId)
    {
        var node = session.Tree.Find(nodeId)!;
        var id = node.ObjectId!.Value;
        return session.Document.Objects.FindId(id) is { IsHidden: false, Visible: true } obj &&
            (session.InputVisibility.IsVisible(id) || session.EditScope.ParentId == node.ParentId ||
             session.SelectedNodeId == nodeId || obj.IsSelected(false) > 0);
    }

    private static bool ControlHit(DocumentSession session, Guid controlId, PickContext pick,
        out double depth, out double distance)
    {
        if (session.ControlCage(controlId) is { } cage)
            return PickGeometry.TestCurves(pick, cage.Wires, cage.Bounds, out depth, out distance);
        depth = double.NegativeInfinity;
        distance = double.PositiveInfinity;
        if (session.Tree.Find(controlId)?.ObjectId is not { } objectId ||
            session.Document.Objects.FindId(objectId)?.Geometry is not Brep raw) return false;
        // Mirror planes and damaged boxes use the same visible raw wire fallback.
        if (session.ControlDisplayTransform(objectId) is not { } transform)
            return PickGeometry.Test(pick, raw, false, out depth, out distance, session.Document.ModelAbsoluteTolerance);
        using var moved = raw.DuplicateBrep();
        return moved.Transform(transform) &&
            PickGeometry.Test(pick, moved, false, out depth, out distance, session.Document.ModelAbsoluteTolerance);
    }

    public static bool OnlyHiddenInputHit(DocumentSession session, RhinoView view, Point point)
    {
        using var pick = Context(view, point);
        var hits = session.Document.Objects.PickObjects(pick);
        try { return hits is { Length: > 0 } && hits.All(hit => Suppressed(session, hit.ObjectId)); }
        finally { if (hits is not null) foreach (var hit in hits) hit.Dispose(); }
    }

    public static bool HasVisibleHit(DocumentSession session, RhinoView view, Point point)
    {
        using var pick = Context(view, point);
        var hits = session.Document.Objects.PickObjects(pick);
        try { if (hits is not null && hits.Any(hit => !Suppressed(session, hit.ObjectId))) return true; }
        finally { if (hits is not null) foreach (var hit in hits) hit.Dispose(); }
        using var mode = view.ActiveViewport.DisplayMode;
        foreach (var root in session.Tree.Roots)
            if (session.Working.IsVisible(root) &&
                (session.Tree.Find(root)?.ObjectId is not { } source || session.Document.Objects.FindId(source) is { IsHidden: false, Visible: true }) &&
                session.DisplayResult(root) is { HasResult: true } result)
                foreach (var brep in result.Results)
                    if (PickGeometry.Test(pick, brep, mode.DisplayAttributes.ShadingEnabled, out _, out _, session.Document.ModelAbsoluteTolerance)) return true;
        foreach (var control in session.Tree.Nodes.Where(node => node.IsControl && VisibleControl(session, node.Id)))
            if (ControlHit(session, control.Id, pick, out _, out _)) return true;
        return false;
    }

    public static Guid? Pick(DocumentSession session, RhinoView view, Point point)
    {
        using var pick = Context(view, point);
        using var mode = view.ActiveViewport.DisplayMode;
        Guid? best = null;
        var bestDepth = double.NegativeInfinity;
        var bestDistance = double.PositiveInfinity;
        void Consider(Guid id, double depth, double distance)
        {
            // In editing scope, the visible wire nearest the cursor wins.
            var better = session.EditScope.ParentId.HasValue
                ? distance < bestDistance - 0.001 || Math.Abs(distance - bestDistance) < 0.001 && depth > bestDepth
                : depth > bestDepth;
            if (!better) return;
            best = id; bestDepth = depth; bestDistance = distance;
        }
        void Test(Guid id, Brep brep, bool faces)
        {
            if (PickGeometry.Test(pick, brep, faces, out var depth, out var distance, session.Document.ModelAbsoluteTolerance))
                Consider(id, depth, distance);
        }
        void TestControl(Guid controlId, Guid selectedId)
        {
            if (ControlHit(session, controlId, pick, out var depth, out var distance)) Consider(selectedId, depth, distance);
        }
        foreach (var id in session.EditScope.Candidates)
        {
            var node = session.Tree.Find(id)!;
            if (node.IsModifier && !session.Working.IsVisible(id)) continue;
            if (node.IsControl)
            {
                if (VisibleControl(session, id)) TestControl(id, id);
                continue;
            }
            if (node.ObjectId is { } source && session.Document.Objects.FindId(source) is not { IsHidden: false, Visible: true }) continue;
            if (session.DisplayResult(id) is not { HasResult: true } result) continue;
            foreach (var brep in result.Results)
                Test(id, brep, !node.IsControl && !session.EditScope.ParentId.HasValue && mode.DisplayAttributes.ShadingEnabled);
        }
        // InputWire is display-only. Native hidden originals never become pick/snap targets.
        foreach (var input in session.Evaluator.InputPreviews)
        {
            if (!session.InputVisibility.IsVisible(input.ObjectId)) continue;
            if (session.Document.Objects.FindId(input.ObjectId) is not { IsHidden: false, Visible: true }) continue;
            var source = session.Tree.FindSource(input.ObjectId);
            if (source is not null && session.EditScope.Resolve(source.Id) is { } id) Test(id, input.Geometry, false);
        }
        foreach (var control in session.Tree.Nodes.Where(node => node.IsControl && VisibleControl(session, node.Id)))
            if (session.EditScope.Resolve(control.Id) is { } id) TestControl(control.Id, id);
        if (best is null) return null;
        // Leave ordinary Rhino objects in front of the preview selectable.
        var nativeHits = session.Document.Objects.PickObjects(pick);
        try
        {
            if (nativeHits is not null)
                foreach (var hit in nativeHits)
                {
                    if (Suppressed(session, hit.ObjectId)) continue;
                    var p = hit.SelectionPoint();
                    if (p.IsValid && pick.PickFrustumTest(p, out var depth, out _) && depth > bestDepth + 1e-8) return null;
                }
        }
        finally { if (nativeHits is not null) foreach (var hit in nativeHits) hit.Dispose(); }
        return best;
    }
}
