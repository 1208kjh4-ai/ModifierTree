using ModifierTree.Core;
using Rhino;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Writes only owned sources. The calling Rhino command owns the Undo record.</summary>
internal static class SubtreeTransform
{
    public static bool Validate(RhinoDoc doc, ModifierTreeModel tree, Guid nodeId, out string error)
    {
        error = "";
        var ids = tree.SourcesInSubtree(nodeId);
        if (ids.Count == 0) { error = "The selected item has no source objects to move."; return false; }
        foreach (var id in ids)
        {
            var obj = doc.Objects.FindId(id);
            if (obj is null) { error = "A source is missing. Restore it before moving this subtree."; return false; }
            if (obj.IsLocked || obj.IsReference || obj.IsHidden || obj.GripsOn)
            { error = "A source is locked, hidden in Rhino, referenced, or has control points on. Make all sources editable first."; return false; }
        }
        return true;
    }

    public static bool Apply(RhinoDoc doc, ModifierTreeModel tree, Guid nodeId, Transform transform, out string error)
    {
        if (!Validate(doc, tree, nodeId, out error)) return false;
        if (!transform.IsValid) { error = "Invalid transformation."; return false; }
        if (transform == Transform.Identity) return true;
        // Preflight every geometry before writing any object. Keep snapshots for rollback.
        var originals = new Dictionary<Guid, GeometryBase>();
        var changed = new List<Guid>();
        try
        {
            foreach (var id in tree.SourcesInSubtree(nodeId))
            {
                var original = doc.Objects.FindId(id)!.Geometry.Duplicate();
                originals.Add(id, original);
                using var candidate = original.Duplicate();
                if (!candidate.Transform(transform) || !candidate.IsValid)
                    throw new InvalidOperationException("A source cannot accept this transformation.");
            }
            foreach (var id in originals.Keys)
            {
                var transformed = doc.Objects.Transform(id, transform, true);
                if (transformed == Guid.Empty) throw new InvalidOperationException("Rhino could not transform a source.");
                changed.Add(id);
                if (transformed != id) throw new InvalidOperationException("Rhino changed a source identity unexpectedly.");
            }
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            foreach (var id in changed.AsEnumerable().Reverse())
                restored &= doc.Objects.Replace(id, originals[id], true);
            error = exception.Message + (restored ? " No source changes were kept." : " Rollback failed for a source; use Undo to restore the command.");
            return false;
        }
        finally { foreach (var geometry in originals.Values) geometry.Dispose(); }
    }
}
