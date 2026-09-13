using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino;
using RhinoCommand = Rhino.Commands.Command;

namespace ModifierTree.Rhino.UI;

[Guid("DECF1714-D81A-4FB8-B893-0887492BA34A")]
public sealed class ModifierManagerPanel : Panel
{
    private const string DragFormat = "ModifierTree.Nodes";
    private readonly DocumentSession _session;
    private readonly TreeGridView _tree = new() { AllowMultipleSelection = true, AllowDrop = true, Size = new Size(100, 100) };
    private readonly Dictionary<Guid, NodeRow> _rows = [];
    private readonly Button _remove = new() { Text = "Remove", Enabled = false };
    private readonly Button _select = new() { Text = "Select in Rhino", Enabled = false };
    private readonly Button _bake = new() { Text = "Bake", Enabled = false, ToolTip = "Keep this Modifier and add its fixed result at the top of the tree." };
    private readonly Button _merge = new() { Text = "Merge", Enabled = false, ToolTip = "Replace this Modifier and its children with one fixed result. Original objects are kept hidden; Undo restores them." };
    private readonly Button _duplicate = new() { Text = "Duplicate subtree", Enabled = false, ToolTip = "Duplicate this Modifier, its children, and independent copies of its Rhino source objects." };
    private readonly ModifierPropertiesEditor _properties = new();
    private readonly CheckBox _preview = new() { Text = "Show result", Checked = true };
    private readonly CheckBox _inputVisible = new() { Text = "Show input wires", ToolTip = "Persistent visibility for this input; selection temporarily shows its edges.", Checked = false, Enabled = false };
    private bool _updatingSelection;
    private bool _refreshingTree;
    private bool _editingName;
    private readonly UITimer _editMonitor = new() { Interval = 0.1 };
    private string? _originalName;
    private readonly ContextMenu _modifierMenu = new();
    private PointF? _dragOrigin;
    private Guid? _dragNode;
    private Guid[]? _selectOnRefresh;
    private Guid[] _panelSelection = [];
    private Guid? _lastSessionSelection;
    private long _lastViewportSelectionRevision;
    private bool _dragging;
    private bool _refreshAfterMouseUp;
    private bool Editable => _session.CanEditTree && !RhinoCommand.InCommand() && RhinoDoc.ActiveDoc?.RuntimeSerialNumber == _session.Document.RuntimeSerialNumber;
    private NodeRow[] SelectedRows => _tree.SelectedItems.OfType<NodeRow>().ToArray();
    private NodeRow? SingleSelectedRow => SelectedRows is [var row] ? row : null;

    public ModifierManagerPanel(RhinoDoc document)
    {
        _session = ModifierTreePlugIn.Instance.Session(document);
        var addObject = new Button { Text = "+ Add Object" };
        addObject.Click += (_, _) => { if (Editable) RhinoApp.RunScript("_MTreeAddObjects", false); };
        var addModifier = new Button { Text = "+ Add Modifier" };
        foreach (var kind in new[] { TreeNodeKind.BooleanDifference, TreeNodeKind.BooleanUnion, TreeNodeKind.BooleanIntersection, TreeNodeKind.Mirror, TreeNodeKind.Array, TreeNodeKind.Bend })
        {
            var item = new ButtonMenuItem { Text = ModifierNames.TypeName(kind) };
            item.Click += (_, _) => { if (Editable && _session.AddModifier(kind) is var id && id != Guid.Empty) _selectOnRefresh = [id]; };
            _modifierMenu.Items.Add(item);
        }
        addModifier.Click += (_, _) => { if (Editable) _modifierMenu.Show(addModifier); };
        _select.Click += (_, _) => SelectSource();
        _bake.Click += (_, _) => { if (Editable && !_editingName) RhinoApp.RunScript("_MTreeBake", false); };
        _merge.Click += (_, _) => { if (Editable && !_editingName) RhinoApp.RunScript("_MTreeMerge", false); };
        _duplicate.Click += (_, _) => { if (Editable && !_editingName) RhinoApp.RunScript("_MTreeDuplicate", false); };
        _properties.EnabledRequested += (_, e) =>
        {
            if (Editable && !_editingName && !_session.SetModifierEnabled(e.NodeId, e.Enabled, out var error))
                _properties.ShowError(error);
            _properties.Bind(_session.Tree.Find(e.NodeId), Editable && !_editingName);
        };
        _properties.ApplyRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            if (!_session.UpdateModifierProperties(e.NodeId, e.Name, e.Enabled, e.Mirror, e.Array, out var error))
                _properties.ShowError(error);
        };
        _properties.ControlBoxApplyRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            if (!_session.UpdateControlBoxProperties(e.NodeId, e.Name, e.Settings, out var error))
                _properties.ShowError(error);
        };
        _properties.AddControlBoxRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            if (!_session.AddControlBox(e.NodeId, out var error)) _properties.ShowError(error);
        };
        _properties.FitControlBoxRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            if (!_session.FitControlBox(e.NodeId, out var error)) _properties.ShowError(error);
        };
        _properties.SetPlaneRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            _session.SelectedNodeId = e.NodeId;
            RhinoApp.RunScript("_MTreeSetPlane", false);
        };
        _properties.SetAxisRequested += (_, e) =>
        {
            if (!Editable || _editingName) return;
            _session.SelectedNodeId = e.NodeId;
            if (e.Reset)
            {
                var direction = e.AxisIndex switch { 0 => global::Rhino.Geometry.Vector3d.XAxis, 1 => global::Rhino.Geometry.Vector3d.YAxis, _ => global::Rhino.Geometry.Vector3d.ZAxis };
                if (!_session.SetArrayAxis(e.NodeId, e.AxisIndex, direction, out var error)) _properties.ShowError(error);
            }
            else RhinoApp.RunScript("_MTreeSetAxis " + "XYZ"[e.AxisIndex], false);
        };
        _tree.CellDoubleClick += DoubleClickCell;
        _tree.CellEdited += EndNameEdit;
        _editMonitor.Elapsed += (_, _) => { if (_editingName && !_tree.IsEditing) FinishNameEdit(); };
        _remove.Click += (_, _) => RemoveSelected();
        _preview.CheckedChanged += (_, _) => { if (!_updatingSelection && Editable) _session.SetPreviewEnabled(_preview.Checked == true); };
        _inputVisible.CheckedChanged += (_, _) =>
        {
            if (_updatingSelection || !Editable || SingleSelectedRow is not { ObjectId: { } id }) return;
            _session.SetInputVisible(id, _inputVisible.Checked == true);
        };
        _tree.Columns.Add(new GridColumn { HeaderText = "Name", DataCell = new TextBoxCell(0), Editable = false, AutoSize = false, Width = 140, MinWidth = 1 });
        _tree.Columns.Add(new GridColumn { HeaderText = "State", HeaderToolTip = "Double-click a Modifier state to turn its operation ON or OFF.", DataCell = new TextBoxCell(1), AutoSize = false, Width = 70, MinWidth = 1 });
        _tree.Columns.Add(new GridColumn { HeaderText = "InputWire", HeaderToolTip = "Double-click to toggle input wires. Selection temporarily reveals edges.", DataCell = new TextBoxCell(2), AutoSize = false, Width = 65, MinWidth = 1 });
        _tree.SelectionChanged += (_, _) =>
        {
            if (_refreshingTree) return;
            var current = SelectedRows.Select(row => row.Key).ToArray();
            if (SelectionInteractionPolicy.PanelSelectionChanged(_panelSelection, current))
                UpdateSelection(selectInViewport: true);
        };
        _tree.CellFormatting += (_, e) => { if (e.Item is NodeRow { IsDisabled: true }) e.ForegroundColor = Colors.Gray; };
        _tree.MouseDown += BeginPossibleDrag;
        _tree.MouseMove += StartDrag;
        _tree.MouseUp += (_, _) =>
        {
            _dragOrigin = null;
            _dragNode = null;
            if (_refreshAfterMouseUp) { _refreshAfterMouseUp = false; _session.RequestRefresh(); }
        };
        _tree.DragEnter += DragOverTree;
        _tree.DragOver += DragOverTree;
        _tree.DragDrop += DropOnTree;

        var rootDrop = new Label { Text = "Drop here to move to root", AllowDrop = true, TextAlignment = TextAlignment.Center, Wrap = WrapMode.Word, Width = 1, Height = 28 };
        rootDrop.DragEnter += DragOverRoot;
        rootDrop.DragOver += DragOverRoot;
        rootDrop.DragDrop += (_, e) =>
        {
            e.Effects = DragEffects.None;
            if (!TryReadDrag(e, out var ids)) return;
            if (_session.MoveNodes(ids, null, _session.Tree.Roots.Count, out var dropError))
            { _selectOnRefresh = ids; e.Effects = DragEffects.Move; }
            else RhinoApp.WriteLine(dropError);
        };

        Content = ManagerLayout.Create(_tree, addObject, addModifier, rootDrop, _select, _duplicate, _bake, _merge, _remove, _preview, _inputVisible, _properties);
        _session.Changed += RefreshTree;
        RefreshTree(this, EventArgs.Empty);
    }

    private void DoubleClickCell(object? sender, GridCellMouseEventArgs e)
    {
        if (!Editable || _editingName || e.Item is not NodeRow row || _session.Tree.Find(row.Key)?.Kind == TreeNodeKind.BasePlane) return;
        _dragOrigin = null;
        _dragNode = null;
        _tree.SelectedItem = row;
        if (e.Column == 0)
        {
            if (row.ObjectId is { } objectId)
            {
                if (_session.Document.Objects.FindId(objectId) is not { } obj) return;
                _originalName = obj.Attributes.Name;
            }
            else
            {
                if (_session.Tree.Find(row.Key) is not { IsModifier: true } modifier) return;
                _originalName = modifier.Name;
            }
            _editingName = true;
            _tree.Columns[0].Editable = true;
            // Edit only the actual name, excluding generated labels and Modifier type prefixes.
            row.SetValue(0, _originalName);
            _tree.BeginEdit(e.Row, e.Column);
            _editMonitor.Start();
        }
        else if (e.Column == 1 && _session.Tree.Find(row.Key) is { IsModifier: true } modifier)
        {
            if (!_session.SetModifierEnabled(row.Key, !modifier.Enabled, out var error)) _properties.ShowError(error);
        }
        else if (e.Column == 2 && row.ObjectId is { } inputId && _session.Tree.Find(row.Key) is { IsControl: false, ParentId: not null })
        {
            _session.SetInputVisible(inputId, !_session.InputVisibility.IsVisible(inputId));
        }
    }

    private void EndNameEdit(object? sender, GridViewCellEventArgs e)
    {
        if (!_editingName) return;
        if (Editable && e.Item is NodeRow row)
        {
            var name = row.GetValue(0)?.ToString() ?? "";
            string error;
            var node = _session.Tree.Find(row.Key);
            var renamed = node is { Kind: TreeNodeKind.ControlBox, ControlBox: { } settings }
                ? _session.UpdateControlBoxProperties(row.Key, name, settings, out error)
                : row.ObjectId is { } id
                    ? SourceNameEditor.Rename(_session.Document, id, name, out error)
                    : _session.RenameModifier(row.Key, name, out error);
            if (!renamed)
            {
                row.SetValue(0, _originalName ?? "");
                RhinoApp.WriteLine(error);
            }
        }
        FinishNameEdit();
    }

    private void FinishNameEdit()
    {
        _editingName = false;
        _editMonitor.Stop();
        _tree.Columns[0].Editable = false;
        _originalName = null;
        _session.RequestRefresh();
        _session.Document.Views.Redraw();
    }

    private void BeginPossibleDrag(object? sender, MouseEventArgs e)
    {
        _dragOrigin = null;
        _dragNode = null;
        if (!Editable || _editingName || (e.Buttons & MouseButtons.Primary) == 0) return;
        if (_tree.GetCellAt(e.Location)?.Item is not NodeRow row) return;
        _dragOrigin = e.Location;
        _dragNode = row.Key;
    }

    private void StartDrag(object? sender, MouseEventArgs e)
    {
        if ((e.Buttons & MouseButtons.Primary) == 0)
        {
            _dragOrigin = null;
            _dragNode = null;
            if (_refreshAfterMouseUp) { _refreshAfterMouseUp = false; _session.RequestRefresh(); }
            return;
        }
        if (!Editable || _session.IsStateRefreshPending || _dragging || _dragOrigin is not { } origin || _dragNode is not { } id || (e.Buttons & MouseButtons.Primary) == 0) return;
        if (Math.Abs(e.Location.X - origin.X) + Math.Abs(e.Location.Y - origin.Y) < 6) return;
        _dragOrigin = null;
        _dragNode = null;
        using var data = new DataObject();
        var selected = SelectedRows.Select(row => row.Key).ToArray();
        // Read after native Shift/Ctrl selection has completed. A selected row keeps the batch
        // during mouse-down; Eto's native handler handles ordinary click/mouse-up selection.
        var requested = selected.Contains(id) ? selected : [id];
        if (requested.Any(key => _session.Tree.Find(key) is null)) { _session.RequestRefresh(); return; }
        var ids = _session.Tree.NormalizeMoveNodes(requested);
        if (ids.Count == 0) return;
        data.SetString(string.Join("\n", ids), DragFormat);
        _dragging = true;
        try { _tree.DoDragDrop(data, DragEffects.Move); }
        finally
        {
            _dragging = false;
            _session.RequestRefresh();
        }
    }

    private bool TryReadDrag(DragEventArgs e, out Guid[] ids)
    {
        ids = [];
        if (!Editable || !ReferenceEquals(e.Source, _tree)) return false;
        var payload = e.Data.GetString(DragFormat);
        if (string.IsNullOrEmpty(payload) || payload.Length > TreeStateCodec.MaxNodeCount * 37) return false;
        var parsed = new List<Guid>();
        foreach (var value in payload.Split('\n'))
        {
            if (!Guid.TryParse(value, out var id) || _session.Tree.Find(id) is null) return false;
            parsed.Add(id);
        }
        ids = _session.Tree.NormalizeMoveNodes(parsed).ToArray();
        return ids.Length > 0;
    }

    private bool DropTarget(DragEventArgs e, out Guid[] ids, out Guid? parentId, out int index)
    {
        parentId = null;
        index = 0;
        if (!TryReadDrag(e, out ids)) return false;
        var info = _tree.GetDragInfo(e);
        if (info.Item is NodeRow over && _session.Tree.Find(over.Key)?.IsModifier != true) info.RestrictToInsert();
        if (info.Position == GridDragPosition.Over && info.Item is NodeRow target)
        {
            parentId = target.Key;
            index = _session.Tree.Find(target.Key)!.Children.Count;
        }
        else if (info.Item is NodeRow sibling)
        {
            parentId = _session.Tree.Find(sibling.Key)!.ParentId;
            index = _session.Tree.ChildrenOf(parentId).ToList().IndexOf(sibling.Key) + (info.Position == GridDragPosition.After ? 1 : 0);
        }
        else
        {
            parentId = (info.Parent as NodeRow)?.Key;
            index = info.InsertIndex < 0 ? _session.Tree.ChildrenOf(parentId).Count : info.InsertIndex;
        }
        return _session.Tree.CanMoveMany(ids, parentId, index, out _);
    }

    private void DragOverTree(object? sender, DragEventArgs e) => e.Effects = DropTarget(e, out _, out _, out _) ? DragEffects.Move : DragEffects.None;
    private void DragOverRoot(object? sender, DragEventArgs e) => e.Effects = TryReadDrag(e, out var ids) &&
        _session.Tree.CanMoveMany(ids, null, _session.Tree.Roots.Count, out _) ? DragEffects.Move : DragEffects.None;

    private void DropOnTree(object? sender, DragEventArgs e)
    {
        e.Effects = DragEffects.None;
        if (!DropTarget(e, out var ids, out var parent, out var index)) return;
        if (_session.MoveNodes(ids, parent, index, out var error)) { _selectOnRefresh = ids; e.Effects = DragEffects.Move; }
        else RhinoApp.WriteLine(error);
    }

    private void RefreshTree(object? sender, EventArgs e)
    {
        if (_dragging || _editingName) return;
        if (_dragOrigin.HasValue)
        {
            // A release outside the panel can bypass its MouseUp callback.
            if ((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) != 0)
            { _refreshAfterMouseUp = true; return; }
            _dragOrigin = null;
            _dragNode = null;
            _refreshAfterMouseUp = false;
        }
        // Session picks (viewport, newly created result) replace a panel batch. Ordinary
        // redraw/name/status refreshes keep all panel rows selected by their stable IDs.
        var selected = _selectOnRefresh ?? (_session.SelectedNodeId != _lastSessionSelection ||
            _session.ViewportSelectionRevision != _lastViewportSelectionRevision
            ? _session.SelectedNodeId is { } active ? [active] : Array.Empty<Guid>()
            : _panelSelection);
        _selectOnRefresh = null;
        var collapsed = _rows.Values.Where(row => row.Children.Count > 0 && !row.Expanded).Select(row => row.Key).ToHashSet();
        _rows.Clear();
        NodeRow Build(Guid id)
        {
            var node = _session.Tree.Find(id)!;
            var state = _session.Evaluator.Find(id);
            string name, status;
            if (node.IsControl)
            {
                var control = node.ObjectId is { } controlId ? _session.Document.Objects.FindId(controlId) : null;
                name = node.Kind == TreeNodeKind.ControlBox
                    ? string.IsNullOrWhiteSpace(control?.Attributes.Name) ? "Control Box" : control.Attributes.Name
                    : "BasePlane";
                status = control is not null ? "Control" : "Missing";
            }
            else if (node.ObjectId is { } objectId)
            {
                var obj = _session.Document.Objects.FindId(objectId);
                name = obj is null ? $"Missing {objectId.ToString()[..8]}" : string.IsNullOrWhiteSpace(obj.Attributes.Name) ? $"{obj.ObjectType}_{objectId.ToString()[..8]}" : obj.Attributes.Name;
                status = obj is null ? "Missing" : state?.Error is not null ? "Invalid input" : "Registered";
            }
            else { name = ModifierNames.DisplayName(node); status = node.Enabled ? state?.Status ?? "Needs inputs" : "OFF"; }
            if (_session.EditScope.ParentId == id) status = "Editing";
            if (node.IsModifier && !node.Enabled && _session.EditScope.ParentId == id) status = "OFF / Editing";
            var visibility = !node.IsControl && node.ObjectId is { } sourceId && node.ParentId.HasValue
                ? _session.InputVisibility.IsVisible(sourceId) ? "ON" : "OFF" : "";
            var row = new NodeRow(id, node.ObjectId, name, status, visibility, node.IsModifier && !node.Enabled) { Expanded = !collapsed.Contains(id) };
            _rows.Add(id, row);
            foreach (var child in node.Children) row.Children.Add(Build(child));
            return row;
        }
        var store = new TreeGridItemCollection();
        foreach (var root in _session.Tree.Roots) store.Add(Build(root));
        // A viewport pick may address a row under a collapsed parent.
        foreach (var selectedId in selected)
            for (var parent = _session.Tree.Find(selectedId)?.ParentId;
                 parent is { } parentId; parent = _session.Tree.Find(parentId)?.ParentId)
                _rows[parentId].Expanded = true;
        _refreshingTree = true;
        try
        {
            _tree.DataStore = store;
            TreeGridSelection.Restore(_tree, selected.Select(key => _rows.GetValueOrDefault(key)).OfType<NodeRow>());
        }
        finally { _refreshingTree = false; }
        UpdateSelection();
    }

    private void UpdateSelection(bool selectInViewport = false)
    {
        var selection = SelectedRows;
        _panelSelection = selection.Select(item => item.Key).ToArray();
        var row = selection is [var single] ? single : null;
        _session.SelectedNodeId = row?.Key;
        // Only an interactive single-row selection acts like Select in Rhino. A tree
        // refresh must not reselect native objects or collapse a Shift/Ctrl drag batch.
        if (selectInViewport && row is not null && !_editingName && !_dragging &&
            !_session.IsMoving && !_session.IsStateRefreshPending &&
            _session.Tree.SourcesInSubtree(row.Key).Count > 0)
            SelectSource();
        // SelectInViewport changes the viewport revision. Remember our own change so
        // the next refresh does not mistake it for a new pick made in the viewport.
        _lastSessionSelection = _session.SelectedNodeId;
        _lastViewportSelectionRevision = _session.ViewportSelectionRevision;
        _remove.Enabled = _session.CanEditTree && row is not null && _session.Tree.Find(row.Key)?.Kind != TreeNodeKind.BasePlane;
        _select.Enabled = row is not null && _session.Tree.SourcesInSubtree(row.Key).Count > 0;
        _updatingSelection = true;
        try
        {
            _preview.Checked = _session.PreviewEnabled;
            _preview.Enabled = _session.CanEditTree;
            _inputVisible.Enabled = _session.CanEditTree && row?.ObjectId is not null && _session.Tree.Find(row.Key) is { IsControl: false, ParentId: not null };
            _inputVisible.Checked = _inputVisible.Enabled && _session.InputVisibility.IsVisible(row!.ObjectId!.Value);
        }
        finally { _updatingSelection = false; }
        _remove.ToolTip = row is not null && _session.Tree.Find(row.Key)?.Kind == TreeNodeKind.ControlBox
            ? "Remove this Control Box and its Bend influence. Undo restores it."
            : "Unregister an object, or remove a Modifier and keep its children.";
        var result = row is null ? null : _session.Evaluator.Find(row.Key);
        _bake.Enabled = _merge.Enabled = _session.CanEditTree && row is { ObjectId: null } &&
            result is { IsCurrent: true, HasResult: true } && result.Results.Count > 0;
        _duplicate.Enabled = _session.CanEditTree && row is { ObjectId: null };
        var propertyNode = row is null ? null : _session.Tree.Find(row.Key);
        var controlName = propertyNode is { Kind: TreeNodeKind.ControlBox, ObjectId: { } controlId }
            ? _session.Document.Objects.FindId(controlId)?.Attributes.Name : null;
        _properties.Bind(propertyNode, Editable && !_editingName, controlName);
        _tree.ToolTip = _session.ArchiveError ?? result?.Error ?? (_session.EditScope.ParentId.HasValue ? "Editing inputs: pick an edge. Double-click a child Modifier to enter it; Esc exits one level." : "Click a viewport result to select its Modifier. Double-click to edit its inputs.");
        _session.Document.Views.Redraw();
    }

    private void RemoveSelected()
    {
        if (!Editable || SingleSelectedRow is not { } row) return;
        var node = _session.Tree.Find(row.Key);
        if (node?.Kind == TreeNodeKind.BasePlane) return;
        if (node is { IsControl: false, ObjectId: not null, ParentId: not null } &&
            MessageBox.Show("Remove this input from the tree? The Rhino object will be kept.", "Modifier Tree", MessageBoxButtons.OKCancel) != DialogResult.Ok) return;
        _session.RemoveNode(row.Key);
    }

    private void SelectSource()
    {
        if (!Editable || SingleSelectedRow is not { } row) return;
        _session.SelectInViewport(row.Key);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _session.Changed -= RefreshTree; _editMonitor.Stop(); _editMonitor.Dispose(); _modifierMenu.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class NodeRow(Guid key, Guid? objectId, string name, string state, string visibility, bool isDisabled) : TreeGridItem(name, state, visibility)
    {
        public Guid Key { get; } = key;
        public Guid? ObjectId { get; } = objectId;
        public bool IsDisabled { get; } = isDisabled;
    }
}
