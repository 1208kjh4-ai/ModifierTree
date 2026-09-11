using ModifierTree.Core;
using ModifierTree.Rhino.Persistence;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModifierTree.Rhino.Modifiers;

/// <summary>Commits a working result's transform to its owned inputs and pattern frames in one native Undo record.</summary>
internal static class WorkingResultTransform
{
    public static bool Apply(RhinoDoc doc, TreeDocumentData data, IReadOnlyCollection<Guid> nodeIds,
        Transform transform, Func<Guid, bool> managedHidden, Func<Guid, bool> logicallyLocked, out string error,
        IReadOnlySet<Guid>? alreadyTransformedSources = null)
    {
        error = "";
        if (data.ReadOnlyReason is { } reason) { error = reason; return false; }
        if (doc.IsReadOnly) { error = "The document is read-only."; return false; }
        if (doc.UndoActive || doc.RedoActive) { error = "Wait for Undo or Redo to finish."; return false; }
        if (!transform.IsValid || !transform.IsAffine || !transform.TryGetInverse(out _))
        { error = "The result requires a finite, nonsingular affine transformation."; return false; }

        var before = data.Capture();
        var wasModified = doc.Modified;
        var sources = new Dictionary<Guid, SourceChange>();
        var changed = new List<Guid>();
        uint record = 0;
        try
        {
            var roots = data.Tree.NormalizeMoveNodes(nodeIds);
            if (roots.Count == 0 || roots.Any(id => data.Tree.Find(id) is not { IsModifier: true }))
                throw new InvalidOperationException("Select a Modifier result to transform.");

            // Validate every descendant before replacing even the first native object. A managed
            // hidden flag permits only our presentation hiding; user and layer locks remain binding.
            foreach (var id in roots.SelectMany(data.Tree.SourcesInSubtree).Distinct())
            {
                var source = doc.Objects.FindId(id)
                    ?? throw new InvalidOperationException("Restore the missing inputs before transforming this result.");
                if (logicallyLocked(id) || source.IsLocked || LayerLocked(doc, source.Attributes.LayerIndex) ||
                    source.IsReference || source.GripsOn || source.IsHidden && !managedHidden(id))
                    throw new InvalidOperationException("An input is locked, hidden by the user, referenced, or has control points on. Make its source editable first.");
                var original = source.Geometry.Duplicate();
                GeometryBase? candidate = null;
                try
                {
                    candidate = original.Duplicate();
                    if (alreadyTransformedSources?.Contains(id) != true && !candidate.Transform(transform) || !candidate.IsValid)
                        throw new InvalidOperationException("A source cannot accept this transformation.");
                    sources.Add(id, new SourceChange(original, candidate));
                }
                catch { original.Dispose(); candidate?.Dispose(); throw; }
            }
            if (sources.Count == 0) throw new InvalidOperationException("The selected result has no source objects to transform.");

            var draft = new ModifierTreeModel();
            var wires = new InputVisibility();
            TreeStateCodec.Apply(before, draft, wires);
            foreach (var root in roots)
            {
                foreach (var array in PatternFrameTransform.ArraysInSubtree(draft, root))
                {
                    if (!PatternFrameTransform.TryTransform(array.Array!, transform, out var settings, out var validationError) ||
                        !draft.SetArraySettings(array.Id, settings, out validationError))
                        throw new InvalidOperationException(validationError.Length > 0 ? validationError : "An Array frame cannot accept this transformation.");
                }
                foreach (var mirror in ModifiersInSubtree(draft, root).Where(node => node.Kind == TreeNodeKind.Mirror &&
                             !node.Children.Any(child => draft.Find(child)!.IsControl)))
                {
                    // Legacy Mirror documents have no control object. Carry their fallback plane
                    // with the subtree until Set Plane materializes a native control.
                    var settings = mirror.Mirror!;
                    var plane = new Plane(new Point3d(settings.Origin.X, settings.Origin.Y, settings.Origin.Z),
                        new Vector3d(settings.Normal.X, settings.Normal.Y, settings.Normal.Z));
                    if (!plane.Transform(transform) || !plane.IsValid)
                        throw new InvalidOperationException("A Mirror plane cannot accept this transformation.");
                    var updated = settings with
                    {
                        Origin = new ModifierVector(plane.OriginX, plane.OriginY, plane.OriginZ),
                        Normal = new ModifierVector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z)
                    };
                    if (!draft.SetMirrorSettings(mirror.Id, updated, out var validationError))
                        throw new InvalidOperationException(validationError);
                }
            }
            var after = TreeStateCodec.Capture(draft, wires, before.PreviewEnabled);
            var privateChanged = TreeStateCodec.Encode(before) != TreeStateCodec.Encode(after);
            if (transform == Transform.Identity) return true;

            if (!doc.UndoRecordingIsActive && doc.UndoRecordingEnabled)
            {
                record = doc.BeginUndoRecord("Transform Modifier result");
                if (record == 0) throw new InvalidOperationException("Rhino could not start an Undo record.");
            }
            foreach (var (id, source) in sources)
            {
                // Rhino can have selected both a result and one of its exposed source inputs.
                // That source already belongs to the caller's native transform and Undo record.
                if (alreadyTransformedSources?.Contains(id) == true) continue;
                // Record the attempted replacement first: a native event listener may throw after
                // Rhino has already changed the object, in which case it still needs rollback.
                changed.Add(id);
                if (!doc.Objects.Replace(id, source.Candidate, true))
                    throw new InvalidOperationException("Rhino could not transform an owned source object.");
            }
            if (privateChanged && !data.Edit("Transform Modifier result", (tree, visibility) =>
                { TreeStateCodec.Apply(after, tree, visibility); return true; }, out var editError))
                throw new InvalidOperationException(editError.Length > 0 ? editError : "Rhino could not save the transformed pattern frames.");
            doc.Modified = true;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            try { if (TreeStateCodec.Encode(data.Capture()) != TreeStateCodec.Encode(before)) data.Load(before); }
            catch { restored = false; }
            foreach (var id in changed.AsEnumerable().Reverse())
            {
                try { restored &= doc.Objects.Replace(id, sources[id].Original, true); }
                catch { restored = false; }
            }
            if (restored) doc.Modified = wasModified;
            error = exception.Message + (restored ? "" : " Some changes could not be rolled back; use Undo to restore this action.");
            return false;
        }
        finally
        {
            if (record != 0) doc.EndUndoRecord(record);
            foreach (var source in sources.Values) source.Dispose();
        }
    }

    private static IEnumerable<ModifierTreeNode> ModifiersInSubtree(ModifierTreeModel tree, Guid id)
    {
        if (tree.Find(id) is not { IsModifier: true } node) yield break;
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in ModifiersInSubtree(tree, child)) yield return descendant;
    }

    private static bool LayerLocked(RhinoDoc doc, int index)
    {
        if (index < 0 || index >= doc.Layers.Count) return true;
        Layer? layer = doc.Layers[index];
        var visited = new HashSet<Guid>();
        while (layer is not null)
        {
            if (!visited.Add(layer.Id) || layer.IsLocked) return true;
            if (layer.ParentLayerId == Guid.Empty) return false;
            var parent = layer.ParentLayerId;
            layer = doc.Layers.FirstOrDefault(candidate => candidate.Id == parent);
            if (layer is null) return true;
        }
        return false;
    }

    private sealed record SourceChange(GeometryBase Original, GeometryBase Candidate) : IDisposable
    {
        public void Dispose() { Original.Dispose(); Candidate.Dispose(); }
    }
}
