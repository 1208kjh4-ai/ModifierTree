using ModifierTree.Core;
using ModifierTree.Rhino.Diagnostics;
using ModifierTree.Rhino.Display;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino.Collections;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Environment = System.Environment;

namespace ModifierTree.Rhino;

internal sealed class DocumentSession : IDisposable
{
    private bool _refreshPending;
    private bool _redrawPending;
    private bool _disposed;
    private readonly TreeDocumentData _data;
    private readonly PatternTransformTracker _patternTransforms;
    private readonly WorkingResultTransformEvents _workingTransforms;
    public WorkingResultObjects Working { get; }
    private bool _workingNeedsSync = true;
    private bool _saving;
    private bool _exporting;
    private bool _structureRefreshPending;
    private bool _clearSelectionPending;
    private Guid? _selectAfterRebuild;
    public bool IsReadingDocument { get; private set; }
    public bool IsStateRefreshPending => _structureRefreshPending || IsReadingDocument || _saving;
    public ArchivableDictionary? PreservedArchive { get; private set; }
    public string? ArchiveError => _data.ReadOnlyReason;
    public bool CanEditTree => !IsReadingDocument && ArchiveError is null && !Document.UndoActive && !Document.RedoActive;
    private readonly RebuildGate _rebuild = new();
    private readonly DifferenceConduit _conduit;
    private readonly TreeViewportMouse _viewportMouse;
    private HashSet<Guid> _nativeSelection = [];
    private bool _exitScopePending;
    private Guid? _gumballPendingNode;
    private readonly Eto.Forms.UITimer _liveTimer = new() { Interval = 0.016 };
    private bool _committedSourcesCurrent;
    private readonly Dictionary<Guid, Transform> _moveTransforms = [];
    private readonly BendControlCageCache _controlCages = new();
    private bool _liveUpdating;
    private long _nextLiveUpdate;
    public RhinoDoc Document { get; }
    public ModifierTreeModel Tree => _data.Tree;
    public TreeEvaluator Evaluator { get; } = new();
    public LiveTreePreview LivePreview { get; } = new();
    public EventTrace Trace { get; }
    public bool PreviewEnabled => _data.PreviewEnabled;
    public InputVisibility InputVisibility => _data.InputVisibility;
    public Guid? SelectedNodeId { get; set; }
    public Guid? ViewportSelectedNodeId { get; private set; }
    public long ViewportSelectionRevision { get; private set; }
    public TreeEditScope EditScope { get; }
    public bool IsMoving { get; set; }
    public event EventHandler? Changed;

    public DocumentSession(RhinoDoc document)
    {
        Document = document;
        _data = new TreeDocumentData(document);
        _data.Changed += OnTreeStateChanged;
        Working = new WorkingResultObjects(document);
        _patternTransforms = new PatternTransformTracker(document, _data,
            () => CanEditTree && !IsStateRefreshPending ? ViewportSelectedNodeId : null, isWorkingResult: Working.IsProxy);
        _workingTransforms = new WorkingResultTransformEvents(document, _data, Working, RequestRebuild);
        EditScope = new TreeEditScope(Tree);
        Trace = new EventTrace(document);
        _conduit = new DifferenceConduit(this);
        _viewportMouse = new TreeViewportMouse(this) { Enabled = true };
        RhinoDoc.AddRhinoObject += OnObjectChanged;
        RhinoDoc.DeleteRhinoObject += OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
        RhinoDoc.ReplaceRhinoObject += OnObjectReplaced;
        RhinoDoc.ModifyObjectAttributes += OnAttributesChanged;
        RhinoDoc.DocumentPropertiesChanged += OnDocumentPropertiesChanged;
        RhinoDoc.BeginSaveDocument += OnBeginSave;
        RhinoDoc.EndSaveDocument += OnEndSave;
        Command.EndCommand += OnEndCommand;
        RhinoApp.Idle += OnIdle;
        RhinoApp.EscapeKeyPressed += OnEscape;
        _liveTimer.Elapsed += OnLivePreviewTick;
        _liveTimer.Start();
    }

    public ModifierDocumentState CaptureState() => _data.Capture();

    public void BeginFileRead(bool clearPrevious = false)
    {
        SuspendWorkingObjects();
        IsReadingDocument = true;
        if (!clearPrevious) return;
        PreservedArchive = null;
        _data.ReadOnlyReason = null;
        _data.Load(TreeStateCodec.Capture(new ModifierTreeModel(), new InputVisibility(), true));
    }
    public void EndFileRead() { IsReadingDocument = false; RequestRebuild(); }

    public void RestoreFromFile(ModifierDocumentState state)
    {
        PreservedArchive = null;
        _data.ReadOnlyReason = null;
        _data.Load(state);
    }

    public void PreserveUnreadArchive(ArchivableDictionary? archive, string error)
    {
        PreservedArchive = archive;
        _data.ReadOnlyReason = error;
        RhinoApp.WriteLine(error + (archive is null ? " Saving is blocked to avoid losing the unread tree data." : " The original tree data will be preserved when saving; tree editing is disabled."));
        RequestRefresh();
    }

    private void OnTreeStateChanged(object? sender, TreeStateChangedEventArgs e)
    {
        // Custom Undo handlers may only change private data. Native selection, conduit setup,
        // UI refresh and rebuild are deferred until document reads/Undo/Redo have completed.
        if (!e.GeometryChanged)
        {
            RequestRefresh();
            _redrawPending = true;
            return;
        }
        _committedSourcesCurrent = false;
        ClearLivePreview();
        Evaluator.Reset();
        _structureRefreshPending = true;
        _selectAfterRebuild = null;
        if (e.StructureChanged)
        {
            _clearSelectionPending |= ViewportSelectedNodeId.HasValue;
            ViewportSelectedNodeId = null;
            SelectedNodeId = null;
            EditScope.Reset();
            _exitScopePending = false;
            _gumballPendingNode = null;
        }
        RequestRebuild();
    }

    public bool SelectInViewport(Guid nodeId)
    {
        if (!CanEditTree || Tree.Find(nodeId) is not { } node) return false;
        if (PreviewEnabled)
        {
            RevealSelectionLevel(nodeId);
            RefreshWorkingObjects();
        }
        if (PreviewEnabled && node.IsModifier)
        {
            if (Working.ObjectForNode(nodeId) is not { } proxy)
            { RhinoApp.WriteLine("This Modifier has no current result. Select an input in the panel to edit it."); return false; }
            if (!ValidateMove(nodeId, out var error)) { RhinoApp.WriteLine(error); return false; }
            Working.ClearPendingSelection();
            Document.Objects.UnselectAll();
            if (!Document.Objects.Select(proxy, true, true, true)) return false;
        }
        else if (!SubtreeSelection.Select(Document, Tree, nodeId, out var error))
        { RhinoApp.WriteLine(error); return false; }
        SelectedNodeId = ViewportSelectedNodeId = nodeId;
        ViewportSelectionRevision++;
        _gumballPendingNode = nodeId;
        RememberSelection();
        RequestRefresh();
        Document.Views.Redraw();
        return true;
    }

    private void RememberSelection() => _nativeSelection = Document.Objects.GetSelectedObjects(false, false).Select(obj => obj.Id).ToHashSet();

    public void ClearViewportSelection()
    {
        Working.ClearPendingSelection();
        ViewportSelectionRevision++;
        _gumballPendingNode = null;
        Document.Objects.UnselectAll();
        SelectedNodeId = ViewportSelectedNodeId = null;
        RememberSelection();
        RequestRefresh();
        Document.Views.Redraw();
    }

    public void EnteredScope()
    {
        Working.ClearPendingSelection();
        ViewportSelectionRevision++;
        _gumballPendingNode = null;
        Document.Objects.UnselectAll();
        ViewportSelectedNodeId = null;
        SelectedNodeId = EditScope.ParentId;
        RefreshWorkingObjects();
        RememberSelection();
        RequestRefresh();
        Document.Views.Redraw();
    }

    public void ExitScope()
    {
        if (EditScope.Exit() is not { } exited) return;
        if (!SelectInViewport(exited)) EnteredScope();
    }

    private void OnEscape(object? sender, EventArgs e)
    {
        // Delay selection until Rhino has finished its own Escape/deselection handling.
        if (!_disposed && IsOurDocument(RhinoDoc.ActiveDoc) && !Command.InCommand() && !IsMoving && EditScope.ParentId.HasValue)
            _exitScopePending = true;
    }

    private void SyncNativeSelection()
    {
        var current = Document.Objects.GetSelectedObjects(false, false).Select(obj => obj.Id).ToHashSet();
        if (_nativeSelection.SetEquals(current)) return;
        ViewportSelectionRevision++;
        _nativeSelection = current;
        ViewportSelectedNodeId = null;
        if (current.Count > 0)
        {
            if (current.Count == 1) ViewportSelectedNodeId = Working.NodeForObject(current.First());
            // Prefer the current level, including a Modifier owning exactly this selection.
            foreach (var id in EditScope.Candidates)
                if (ViewportSelectedNodeId is null && current.SetEquals(Tree.SourcesInSubtree(id))) { ViewportSelectedNodeId = id; break; }
            if (ViewportSelectedNodeId is null && current.Count == 1)
                ViewportSelectedNodeId = Tree.FindSource(current.First())?.Id;
        }
        SelectedNodeId = ViewportSelectedNodeId;
        RequestRefresh();
        _redrawPending = true;
    }

    public NodeEvaluation? DisplayResult(Guid nodeId)
    {
        var live = LivePreview.Find(nodeId);
        return live?.HasResult == true ? live : Evaluator.Find(nodeId);
    }

    // Read native transforms directly: cage feedback and cancellation must not wait
    // for the throttled Brep evaluator. An individual source transform takes precedence,
    // matching UpdateLivePreview when a proxy and an exposed input are both selected.
    public Transform? ControlDisplayTransform(Guid objectId)
    {
        if (IsMoving) return _moveTransforms.TryGetValue(objectId, out var move) ? move : null;
        if (Document.Objects.FindId(objectId) is { } source && source.GetDynamicTransform(out var own)) return own;
        foreach (var proxyId in Working.ObjectIds)
            if (Working.NodeForObject(proxyId) is { } nodeId && Document.Objects.FindId(proxyId) is { } proxy &&
                proxy.GetDynamicTransform(out var transform) && Tree.SourcesInSubtree(nodeId).Contains(objectId)) return transform;
        return null;
    }

    // Borrowed display/picking curves. The orthogonal native box stays authoritative
    // for its local frame, document storage, Undo and Rhino's existing Gumball.
    public BendControlCage? ControlCage(Guid nodeId)
    {
        if (Tree.Find(nodeId) is not { Kind: TreeNodeKind.ControlBox, ObjectId: { } objectId, ControlBox: { } settings } ||
            Document.Objects.FindId(objectId) is not { } obj) return null;
        return _controlCages.Get(nodeId, obj.Geometry, settings, Document.ModelAbsoluteTolerance, ControlDisplayTransform(objectId));
    }

    public void PreviewMove(Guid nodeId, Transform transform)
    {
        _moveTransforms.Clear();
        foreach (var id in Tree.SourcesInSubtree(nodeId)) _moveTransforms[id] = transform;
        // Also serve point-getters whose nested message loop delays timer ticks.
        UpdateLivePreview();
    }

    public void ClearLivePreview()
    {
        _moveTransforms.Clear();
        if (LivePreview.Clear()) _redrawPending = true;
        _nextLiveUpdate = 0;
    }

    private void OnLivePreviewTick(object? sender, EventArgs e)
    {
        if (UpdateLivePreview()) Document.Views.Redraw();
    }

    private bool UpdateLivePreview()
    {
        if (_disposed || _liveUpdating || IsStateRefreshPending) return false;
        if (!PreviewEnabled || Document.UndoActive || Document.RedoActive || !IsOurDocument(RhinoDoc.ActiveDoc))
            return LivePreview.Clear();
        var transforms = new Dictionary<Guid, Transform>();
        var transformedModifiers = new HashSet<Guid>();
        if (IsMoving)
        {
            foreach (var pair in _moveTransforms) transforms.Add(pair.Key, pair.Value);
        }
        else
        {
            foreach (var proxy in Working.ObjectIds)
                if (Working.NodeForObject(proxy) is { } nodeId && Document.Objects.FindId(proxy) is { } result &&
                    result.GetDynamicTransform(out var proxyTransform))
                {
                    transformedModifiers.Add(nodeId);
                    foreach (var sourceId in Tree.SourcesInSubtree(nodeId)) transforms[sourceId] = proxyTransform;
                }
            foreach (var node in Tree.Nodes)
                if (node.ObjectId is { } id && node.ParentId.HasValue && Document.Objects.FindId(id) is { } obj &&
                    obj.GetDynamicTransform(out var transform)) transforms[id] = transform;
        }
        // Clearing is never throttled: cancellation immediately restores the committed result.
        if (transforms.Count == 0) return LivePreview.Clear();
        if (Environment.TickCount64 < _nextLiveUpdate) return false;
        _liveUpdating = true;
        var started = Environment.TickCount64;
        try { return LivePreview.Update(Tree, id => Document.Objects.FindId(id)?.Geometry, transforms, Document.ModelAbsoluteTolerance,
            _committedSourcesCurrent ? Evaluator : null, IsMoving ? SelectedNodeId : ViewportSelectedNodeId,
            transformedModifiers.Count > 0 ? transformedModifiers : null); }
        finally
        {
            // Include computation in the 33 ms frame budget; don't add its cost again.
            // Slow calculations still yield to the UI before the next timer tick.
            _nextLiveUpdate = started + 33;
            _liveUpdating = false;
        }
    }

    public bool RegisterSource(Guid objectId) => RegisterSources([objectId]) > 0;

    public int RegisterSources(IEnumerable<Guid> objectIds)
    {
        if (!CanEditTree) return 0;
        var ids = objectIds.Where(id => id != Guid.Empty && !Working.IsProxy(id) &&
            Document.Objects.FindId(id)?.Attributes.GetUserString(WorkingResultObjects.Marker) is null).Distinct().ToArray();
        var added = 0;
        var changed = _data.Edit("Register Modifier Tree objects", (tree, _) =>
        {
            foreach (var id in ids) if (tree.RegisterSource(id)) added++;
            return added > 0;
        }, out var error);
        if (error.Length > 0) RhinoApp.WriteLine(error);
        return changed ? added : 0;
    }

    public Guid AddModifier(TreeNodeKind kind = TreeNodeKind.BooleanDifference)
    {
        if (!CanEditTree) return Guid.Empty;
        SuspendWorkingObjects();
        if (kind == TreeNodeKind.Mirror)
        {
            if (!MirrorPlaneEdit.AddMirror(Document, _data, out var mirrorId, out var mirrorError))
            { RhinoApp.WriteLine(mirrorError); return Guid.Empty; }
            return mirrorId;
        }
        var id = Guid.Empty;
        var changed = _data.Edit("Add Modifier", (tree, _) => { id = tree.AddModifier(kind).Id; return true; }, out var error);
        if (error.Length > 0) RhinoApp.WriteLine(error);
        return changed ? id : Guid.Empty;
    }

    public bool MoveNode(Guid nodeId, Guid? parentId, int index, out string error)
        => MoveNodes([nodeId], parentId, index, out error);

    public bool MoveNodes(IEnumerable<Guid> nodeIds, Guid? parentId, int index, out string error)
    {
        error = "";
        if (!CanEditTree) { error = ArchiveError ?? "Wait for the document operation to finish."; return false; }
        var moveError = "";
        SuspendWorkingObjects();
        var valid = false;
        var ids = nodeIds.ToArray();
        var changed = _data.Edit("Rearrange Modifier Tree", (tree, _) => valid = tree.MoveMany(ids, parentId, index, out moveError), out error);
        if (moveError.Length > 0) error = moveError;
        return changed || valid && error.Length == 0;
    }

    public bool RemoveNode(Guid nodeId)
    {
        if (!CanEditTree) return false;
        SuspendWorkingObjects();
        var changed = TreeItemRemoval.Apply(Document, _data, nodeId, out var error);
        if (error.Length > 0) RhinoApp.WriteLine(error);
        return changed;
    }

    public bool RenameModifier(Guid nodeId, string name, out string error)
    {
        error = "";
        if (!CanEditTree) { error = ArchiveError ?? "Wait for the document operation to finish."; return false; }
        var renameError = "";
        var valid = false;
        var changed = _data.Edit("Rename Modifier", (tree, _) => valid = tree.RenameModifier(nodeId, name, out renameError), out error);
        if (renameError.Length > 0) error = renameError;
        return changed || valid && error.Length == 0;
    }

    public bool CommitResult(Guid nodeId, ResultCommitMode mode, out Guid resultNodeId, out string error)
    {
        resultNodeId = Guid.Empty;
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        SuspendWorkingObjects();
        if (!ResultCommit.Apply(Document, _data, nodeId, mode, out resultNodeId, out error)) return false;
        _selectAfterRebuild = resultNodeId;
        RequestRebuild();
        return true;
    }

    public bool SetModifierEnabled(Guid nodeId, bool enabled, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        return ModifierPropertyEdit.SetEnabled(_data, nodeId, enabled, out error);
    }

    public bool UpdateModifierProperties(Guid nodeId, string name, bool enabled,
        MirrorSettings? mirror, ArraySettings? array, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        return ModifierPropertyEdit.Apply(_data, nodeId, name, enabled, mirror, array, out error);
    }

    public bool DuplicateModifier(Guid nodeId, out Guid copiedNodeId, out string error)
    {
        copiedNodeId = Guid.Empty;
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        SuspendWorkingObjects();
        if (!SubtreeDuplicate.Apply(Document, _data, nodeId, out copiedNodeId, out error)) return false;
        _selectAfterRebuild = copiedNodeId;
        RequestRebuild();
        return true;
    }

    public bool SetMirrorPlane(Guid nodeId, Plane plane, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        SuspendWorkingObjects();
        if (!MirrorPlaneEdit.SetPlane(Document, _data, nodeId, plane, out error)) return false;
        _selectAfterRebuild = nodeId;
        RequestRebuild();
        return true;
    }

    public bool SetArrayAxis(Guid nodeId, int axisIndex, Vector3d direction, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        return ArrayAxisEdit.Apply(_data, nodeId, axisIndex, direction, out error);
    }

    public bool AddControlBox(Guid bendId, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        SuspendWorkingObjects();
        if (!BendControlEdit.Add(Document, _data, bendId, out var controlId, out error)) return false;
        SelectedNodeId = controlId;
        _selectAfterRebuild = controlId;
        RequestRebuild();
        return true;
    }

    public bool FitControlBox(Guid controlId, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        SuspendWorkingObjects();
        if (!BendControlEdit.Fit(Document, _data, controlId, out error)) return false;
        _selectAfterRebuild = controlId;
        RequestRebuild();
        return true;
    }

    public bool UpdateControlBoxProperties(Guid controlId, string name, ControlBoxSettings settings, out string error)
    {
        error = "";
        if (!CanEditTree || IsMoving) { error = ArchiveError ?? "Finish the current document operation first."; return false; }
        // Native attribute Undo must record the original mode, not the temporary
        // presentation Hide that can cease to be owned after entering edit scope.
        if (Tree.Find(controlId)?.ObjectId is { } objectId && Document.Objects.FindId(objectId)?.Attributes.Name != name)
            SuspendWorkingObjects();
        return BendControlEdit.SetProperties(Document, _data, controlId, name, settings, out error);
    }

    public bool ValidateMove(Guid nodeId, out string error)
    {
        error = "";
        var ids = Tree.SourcesInSubtree(nodeId);
        if (!CanEditTree || ids.Count == 0) { error = "Select an editable tree item with source objects."; return false; }
        foreach (var id in ids)
        {
            var obj = Document.Objects.FindId(id);
            if (obj is null || obj.IsReference || obj.GripsOn || Working.Sources.IsLogicallyLocked(id) ||
                obj.IsHidden && !Working.Sources.IsManagedHidden(id))
            { error = "An input is missing, locked, hidden by the user or has control points on. Make the input editable first."; return false; }
        }
        return true;
    }

    public bool MoveTreeNode(Guid nodeId, Transform transform, out string error)
    {
        if (!ValidateMove(nodeId, out error)) return false;
        bool changed;
        if (Tree.Find(nodeId)?.IsModifier == true)
            changed = WorkingResultTransform.Apply(Document, _data, [nodeId], transform,
                Working.Sources.IsManagedHidden, Working.Sources.IsLogicallyLocked, out error);
        else
        {
            SuspendWorkingObjects();
            changed = SubtreeTransform.Apply(Document, Tree, nodeId, transform, out error);
        }
        Working.Invalidate();
        RequestRebuild();
        return changed;
    }

    public bool CanDrawSource(Guid objectId)
    {
        var obj = Document.Objects.FindId(objectId);
        if (obj is null || obj.IsHidden && !Working.Sources.IsManagedHidden(objectId)) return false;
        var layer = Document.Layers.FindIndex(obj.Attributes.LayerIndex);
        while (layer is not null)
        {
            if (!layer.IsVisible) return false;
            layer = layer.ParentLayerId == Guid.Empty ? null : Document.Layers.FindId(layer.ParentLayerId);
        }
        return true;
    }

    private void RevealSelectionLevel(Guid nodeId)
    {
        var parent = Tree.Find(nodeId)?.ParentId;
        if (EditScope.ParentId == parent) return;
        var ancestors = new Stack<Guid>();
        while (parent is { } id) { ancestors.Push(id); parent = Tree.Find(id)?.ParentId; }
        EditScope.Reset();
        while (ancestors.TryPop(out var id)) EditScope.Enter(id);
        Working.ClearPendingSelection();
        Document.Objects.UnselectAll();
        RequestRefresh();
    }

    private void SuspendWorkingObjects()
    {
        Working.Suspend();
        _workingNeedsSync = true;
        RequestRefresh();
    }

    private void RefreshWorkingObjects()
    {
        if (IsReadingDocument || _saving || Document.UndoActive || Document.RedoActive) return;
        try
        {
            Working.Synchronize(Tree, Evaluator, EditScope.ParentId, PreviewEnabled && ArchiveError is null);
            _conduit.SetObjectIdFilter(Tree.Nodes.Where(node => node.ObjectId.HasValue).Select(node => node.ObjectId!.Value).Concat(Working.ObjectIds));
            _workingNeedsSync = false;
        }
        catch (Exception exception)
        {
            _workingNeedsSync = false;
            RhinoApp.WriteLine("Modifier Tree working display: " + exception.Message);
        }
        _redrawPending = true;
    }

    private void OnBeginSave(object? sender, DocumentSaveEventArgs e)
    {
        if (!IsOurDocument(e.Document)) return;
        // The direct Write3dmFile API has no filename in this event. Non-3dm exports keep native results.
        var extension = System.IO.Path.GetExtension(e.FileName ?? "");
        if (e.ExportSelected || extension.Length > 0 && !extension.StartsWith(".3dm", StringComparison.OrdinalIgnoreCase))
        {
            Working.SetExportMarkers(restore: false);
            _exporting = _saving = true;
            return;
        }
        SuspendWorkingObjects();
        _saving = true;
    }

    private void OnEndSave(object? sender, DocumentSaveEventArgs e)
    {
        if (!IsOurDocument(e.Document) || !_saving) return;
        if (_exporting) { Working.SetExportMarkers(restore: true); _exporting = false; }
        _saving = false;
        RequestRefresh();
    }

    public void SetPreviewEnabled(bool enabled)
    {
        if (!CanEditTree) return;
        SuspendWorkingObjects();
        _data.Edit("Change Modifier result display", (_, _) => true, out var error, enabled);
        if (error.Length > 0) RhinoApp.WriteLine(error);
        RequestRefresh();
    }

    public void SetInputVisible(Guid objectId, bool visible)
    {
        if (!CanEditTree || Tree.FindSource(objectId) is null) return;
        _data.Edit("Change Modifier input wires", (_, visibility) => { visibility.Set(objectId, visible); return true; }, out var error);
        if (error.Length > 0) RhinoApp.WriteLine(error);
        RequestRefresh();
    }

    private void StructureChanged()
    {
        _committedSourcesCurrent = false;
        if (SelectedNodeId is { } selected && Tree.Find(selected) is null) SelectedNodeId = null;
        // Old results belong to the old expression and must disappear immediately.
        _conduit.Enabled = false;
        ClearLivePreview();
        Evaluator.Reset();
        _conduit.SetObjectIdFilter(Tree.Nodes.Where(node => node.ObjectId.HasValue).Select(node => node.ObjectId!.Value));
        _conduit.Enabled = Tree.ModifierCount > 0;
        RequestRebuild();
        _redrawPending = true;
    }

    public void RequestRefresh() { _refreshPending = true; _workingNeedsSync = true; }
    public void RequestRebuild() { _rebuild.Request(); RequestRefresh(); }
    private bool IsOurDocument(RhinoDoc? document) => document?.RuntimeSerialNumber == Document.RuntimeSerialNumber;

    private void SourceChanged(Guid objectId)
    {
        if (Tree.FindSource(objectId) is not { } node) return;
        _committedSourcesCurrent = false;
        ClearLivePreview();
        if (node.ParentId.HasValue) RequestRebuild();
        else RequestRefresh();
    }

    private void OnObjectChanged(object? sender, RhinoObjectEventArgs e)
    {
        if (Working.IsBusy || !IsOurDocument(e.TheObject?.Document)) return;
        if (Working.IsProxy(e.ObjectId)) { Working.Invalidate(); RequestRebuild(); return; }
        Working.DetachCopiedMarker(e.ObjectId);
        SourceChanged(e.ObjectId);
    }

    private void OnObjectReplaced(object? sender, RhinoReplaceObjectEventArgs e)
    {
        if (Working.IsBusy || !IsOurDocument(e.OldRhinoObject?.Document)) return;
        if (Working.ObserveReplacement(e.ObjectId, e.OldRhinoObject, e.NewRhinoObject)) { Working.Invalidate(); RequestRebuild(); return; }
        SourceChanged(e.ObjectId);
    }

    private void OnAttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        if (!Working.IsBusy && IsOurDocument(e.Document) && (Tree.FindSource(e.RhinoObject.Id) is not null || Working.IsProxy(e.RhinoObject.Id))) RequestRefresh();
    }

    private void OnDocumentPropertiesChanged(object? sender, DocumentEventArgs e)
    {
        if (!Working.IsBusy && IsOurDocument(e.Document)) { _committedSourcesCurrent = false; ClearLivePreview(); RequestRebuild(); }
    }

    private void OnEndCommand(object? sender, CommandEventArgs e)
    {
        if (IsOurDocument(e.Document))
        {
            _patternTransforms.Clear();
            _workingTransforms.Clear();
            if (e.CommandEnglishName is "Undo" or "Redo" or "UndoMultiple" or "UndoSelected")
            {
                // Undo may restore a native result under a new GUID. Reconcile after
                // the command, even when its object events did not name the current GUID.
                Working.Invalidate();
                RequestRebuild();
            }
            if (_exporting) { Working.SetExportMarkers(restore: true); _exporting = false; }
            _saving = false;
            ClearLivePreview();
            if (Tree.ModifierCount > 0) _redrawPending = true;
        }
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        if (_disposed || IsReadingDocument || _saving || Command.InCommand() || Document.UndoActive || Document.RedoActive) return;
        if (_structureRefreshPending)
        {
            _structureRefreshPending = false;
            _controlCages.Retain(Tree.Nodes.Where(node => node.Kind == TreeNodeKind.ControlBox).Select(node => node.Id));
            if (_clearSelectionPending) { _clearSelectionPending = false; Working.ClearPendingSelection(); Document.Objects.UnselectAll(); RememberSelection(); }
            StructureChanged();
        }
        if (_exitScopePending) { _exitScopePending = false; ExitScope(); }
        if (_rebuild.TryTake(false, false, false))
        {
            ClearLivePreview();
            Evaluator.Rebuild(Tree, id => Document.Objects.FindId(id)?.Geometry, Document.ModelAbsoluteTolerance);
            _committedSourcesCurrent = true;
            _workingNeedsSync = true;
            if (Tree.ModifierCount > 0) RhinoApp.WriteLine($"Modifier Tree rebuilt #{Evaluator.RebuildCount}.");
            _refreshPending = _redrawPending = true;
        }
        if (_workingNeedsSync) RefreshWorkingObjects();
        SyncNativeSelection();
        ActivateSelectionGumball();
        if (_refreshPending)
        {
            if (_selectAfterRebuild is { } resultId)
            {
                _selectAfterRebuild = null;
                if (Tree.Find(resultId) is not null) SelectedNodeId = resultId;
            }
            _refreshPending = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        if (_redrawPending)
        {
            _redrawPending = false;
            Document.Views.Redraw();
        }
    }

    private void ActivateSelectionGumball()
    {
        if (_gumballPendingNode is not { } nodeId) return;
        // A cancelled mouse callback bypasses Rhino's normal post-pick UI update.
        // Finish mouse handling before asking Rhino to activate its native widget.
        if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None) return;
        _gumballPendingNode = null;
        if (!IsOurDocument(RhinoDoc.ActiveDoc) || ViewportSelectedNodeId != nodeId ||
            _nativeSelection.Count == 0 || !_nativeSelection.SetEquals(Working.ObjectForNode(nodeId) is { } proxy ? [proxy] : Tree.SourcesInSubtree(nodeId))) return;
        // Explicit On (never Toggle/Reset) also handles a previously disabled gumball
        // while preserving its alignment and relocation settings. Run once per pick.
        if (!RhinoApp.RunScript(Document.RuntimeSerialNumber, "_Gumball _On", false))
            RhinoApp.WriteLine("Modifier Tree: could not activate Gumball. Run Gumball On to enable it.");
        _redrawPending = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _data.Changed -= OnTreeStateChanged;
        _workingTransforms.Dispose();
        _patternTransforms.Dispose();
        try { Working.Dispose(); }
        catch (Exception exception) { RhinoApp.WriteLine("Modifier Tree display cleanup: " + exception.Message); }
        _data.Dispose();
        _liveTimer.Stop();
        _liveTimer.Elapsed -= OnLivePreviewTick;
        _liveTimer.Dispose();
        RhinoDoc.AddRhinoObject -= OnObjectChanged;
        RhinoDoc.DeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.ReplaceRhinoObject -= OnObjectReplaced;
        RhinoDoc.ModifyObjectAttributes -= OnAttributesChanged;
        RhinoDoc.DocumentPropertiesChanged -= OnDocumentPropertiesChanged;
        RhinoDoc.BeginSaveDocument -= OnBeginSave;
        RhinoDoc.EndSaveDocument -= OnEndSave;
        Command.EndCommand -= OnEndCommand;
        RhinoApp.Idle -= OnIdle;
        RhinoApp.EscapeKeyPressed -= OnEscape;
        _viewportMouse.Dispose();
        _conduit.Dispose();
        _controlCages.Dispose();
        Evaluator.Dispose();
        LivePreview.Dispose();
        Trace.Dispose();
        Changed = null;
    }
}
