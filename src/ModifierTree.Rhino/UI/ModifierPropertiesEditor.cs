using System.Globalization;
using Eto.Drawing;
using Eto.Forms;
using ModifierTree.Core;

namespace ModifierTree.Rhino.UI;

/// <summary>Draft fields survive viewport refreshes until submitted or the selection changes.</summary>
internal sealed class ModifierPropertiesEditor : Panel
{
    private readonly Label _title = Label("Modifier properties");
    private readonly TextBox _name = new() { ID = "modifier-name", Width = 1, PlaceholderText = "Name" };
    private readonly CheckBox _enabled = new() { ID = "modifier-enabled", Text = "Enabled", ToolTip = "OFF keeps the tree and passes through the first input." };
    private readonly Button _setPlane = new() { ID = "mirror-set-plane", Text = "Set Plane" };
    private readonly CheckBox _keepOriginal = new() { ID = "mirror-keep-original", Text = "Keep original" };
    private readonly CheckBox _union = new() { ID = "mirror-union", Text = "Boolean Union", ToolTip = "Union the original and mirrored results. Disconnected solids remain separate." };
    private readonly VectorFields _counts = new("Count (X / Y / Z)", "array-count");
    private readonly VectorFields _spacing = new("Spacing (X / Y / Z)", "array-spacing");
    private readonly Label[] _axisDirections = [Label(""), Label(""), Label("")];
    private readonly StackLayout _mirror;
    private readonly StackLayout _array;
    private readonly Button _addControlBox = new() { ID = "bend-add-control-box", Text = "Add Control Box", ToolTip = "Create a control box fitted to the Bend inputs. Boxes apply in tree order." };
    private readonly Button _fitControlBox = new() { ID = "bend-fit-control-box", Text = "Fit to inputs", ToolTip = "Fit this box to the Bend inputs while keeping its current orientation." };
    private readonly TextBox _strength = new() { ID = "bend-strength", Width = 1, ToolTip = "Bend amount in degrees. Use negative values to reverse the bend. Press Enter to apply." };
    private readonly DropDown _bendMode = new() { ID = "bend-mode", Width = 1, DataStore = new[] { "Limited", "Unlimited" }, SelectedIndex = 0 };
    private readonly StackLayout _bend;
    private readonly StackLayout _controlBox;
    private readonly Label _error = Label("");
    private readonly Button _apply = new() { ID = "modifier-apply", Text = "Apply" };
    private Guid? _nodeId;
    private TreeNodeKind _kind;
    private string? _loadedName;
    private MirrorSettings? _loadedMirror;
    private ArraySettings? _loadedArray;
    private ControlBoxSettings? _loadedControlBox;
    private bool _loading;

    public event EventHandler<ModifierPropertiesEventArgs>? ApplyRequested;
    public event EventHandler<ModifierEnabledEventArgs>? EnabledRequested;
    public event EventHandler<ModifierPlaneEventArgs>? SetPlaneRequested;
    public event EventHandler<ModifierAxisEventArgs>? SetAxisRequested;
    public event EventHandler<ModifierControlBoxEventArgs>? AddControlBoxRequested;
    public event EventHandler<ModifierControlBoxEventArgs>? FitControlBoxRequested;
    public event EventHandler<ControlBoxPropertiesEventArgs>? ControlBoxApplyRequested;

    public ModifierPropertiesEditor()
    {
        _setPlane.ToolTip = "Pick the plane origin, a point on its X axis, and a third point on the plane.";
        _mirror = Stack(_setPlane, Label("Move or rotate BasePlane in the viewport to edit the mirror plane."), _keepOriginal, _union);
        var axes = new List<Control>();
        for (var i = 0; i < 3; i++)
        {
            var axis = i;
            var label = "XYZ"[i];
            _axisDirections[i].ID = "array-axis-" + "xyz"[i];
            var set = new Button { ID = "array-set-axis-" + "xyz"[i], Text = $"Set {label} Axis", ToolTip = "Pick two points to set direction. Spacing stays unchanged." };
            var reset = new Button { ID = "array-reset-axis-" + "xyz"[i], Text = $"Reset {label} Axis", ToolTip = $"Restore the world {label} direction. Spacing stays unchanged." };
            set.Click += (_, _) => RequestAxis(axis, false);
            reset.Click += (_, _) => RequestAxis(axis, true);
            axes.AddRange([_axisDirections[i], set, reset]);
        }
        _array = Stack(_counts, _spacing, Label("Counts include the original. Use 1 on unused axes."), Stack(axes.ToArray()));
        _bend = Stack(_addControlBox, Label("Control Boxes apply from top to bottom. Rotate a box to set its bend direction."));
        _bend.ID = "bend-properties";
        _controlBox = Stack(Label("Strength (degrees)"), _strength, Label("Mode"), _bendMode, _fitControlBox,
            Label("Move, rotate or scale this box with the Gumball. Local Y is the length axis; the bend turns toward local X."));
        _controlBox.ID = "control-box-properties";
        _error.TextColor = Colors.IndianRed;
        _error.Visible = false;
        Content = Stack(_title, Label("Name"), _name, _enabled, _mirror, _array, _bend, _controlBox, _apply, _error);
        _setPlane.Click += (_, _) => RequestPlane();
        _enabled.CheckedChanged += (_, _) =>
        {
            if (!_loading && Enabled && _kind != TreeNodeKind.ControlBox && _nodeId is { } id)
                EnabledRequested?.Invoke(this, new ModifierEnabledEventArgs(id, _enabled.Checked == true));
        };
        _apply.Click += (_, _) => ApplyDraft();
        _name.ToolTip = "Press Enter to apply.";
        _name.KeyDown += SubmitOnEnter;
        _counts.SubmitOnEnter(SubmitOnEnter);
        _spacing.SubmitOnEnter(SubmitOnEnter);
        _strength.KeyDown += SubmitOnEnter;
        _bendMode.SelectedIndexChanged += (_, _) => { if (!_loading && _kind == TreeNodeKind.ControlBox) ApplyDraft(); };
        _addControlBox.Click += (_, _) => RequestAddControlBox();
        _fitControlBox.Click += (_, _) => RequestFitControlBox();
        Visible = false;
    }

    private void SubmitOnEnter(object? sender, KeyEventArgs e)
    {
        if (e.Key != Keys.Enter) return;
        // Consume Enter even for invalid drafts so it cannot repeat a Rhino command.
        e.Handled = true;
        ApplyDraft();
    }

    public void Bind(ModifierTreeNode? node, bool editable, string? controlName = null)
    {
        if (node is null || (!node.IsModifier && node.Kind != TreeNodeKind.ControlBox))
        {
            _nodeId = null;
            Visible = false;
            return;
        }
        var changedSelection = _nodeId != node.Id || _kind != node.Kind;
        _loading = true;
        try
        {
            _nodeId = node.Id;
            _kind = node.Kind;
            var isControlBox = node.Kind == TreeNodeKind.ControlBox;
            var committedName = isControlBox ? controlName ?? "Control Box" : node.Name;
            _title.Text = (isControlBox ? "Control Box" : ModifierNames.TypeName(node.Kind)) + " properties";
            if (changedSelection || _loadedName != committedName) _name.Text = committedName;
            // Always reflect the committed ON/OFF value. Its immediate edit must not discard a numeric draft.
            _enabled.Checked = node.Enabled;
            _enabled.Visible = node.IsModifier;
            _mirror.Visible = node.Kind == TreeNodeKind.Mirror;
            _array.Visible = node.Kind == TreeNodeKind.Array;
            _bend.Visible = node.Kind == TreeNodeKind.Bend;
            _controlBox.Visible = isControlBox;
            _apply.Visible = node.Kind is not (TreeNodeKind.Array or TreeNodeKind.ControlBox);
            if (node.Mirror is { } mirror)
            {
                if (changedSelection || _loadedMirror?.KeepOriginal != mirror.KeepOriginal) _keepOriginal.Checked = mirror.KeepOriginal;
                if (changedSelection || _loadedMirror?.Union != mirror.Union) _union.Checked = mirror.Union;
            }
            if (node.Array is { } array)
            {
                if (changedSelection || _loadedArray?.CountX != array.CountX || _loadedArray?.CountY != array.CountY || _loadedArray?.CountZ != array.CountZ)
                    _counts.Set(new ModifierVector(array.CountX, array.CountY, array.CountZ));
                if (changedSelection || _loadedArray?.Spacing != array.Spacing) _spacing.Set(array.Spacing);
                var directions = new[] { array.AxisX, array.AxisY, array.AxisZ };
                for (var i = 0; i < directions.Length; i++)
                {
                    var v = directions[i];
                    _axisDirections[i].Text = FormattableString.Invariant($"{"XYZ"[i]} axis: ({v.X:G4}, {v.Y:G4}, {v.Z:G4})");
                }
            }
            if (node.ControlBox is { } box)
            {
                if (changedSelection || _loadedControlBox?.Strength != box.Strength)
                    _strength.Text = box.Strength.ToString("G17", CultureInfo.InvariantCulture);
                if (changedSelection || _loadedControlBox?.Limited != box.Limited)
                    _bendMode.SelectedIndex = box.Limited ? 0 : 1;
            }
            _loadedName = committedName;
            _loadedMirror = node.Mirror;
            _loadedArray = node.Array;
            _loadedControlBox = node.ControlBox;
            Enabled = editable;
            Visible = true;
            if (changedSelection) ShowError("");
        }
        finally { _loading = false; }
    }

    public void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = message.Length > 0;
    }

    internal void ApplyDraft()
    {
        if (_loading || !Enabled || _nodeId is not { } id) return;
        if (_kind == TreeNodeKind.ControlBox)
        {
            if (!TryReadControlBoxDraft(out var controlName, out var settings, out var controlError)) { ShowError(controlError); return; }
            ShowError("");
            ControlBoxApplyRequested?.Invoke(this, new ControlBoxPropertiesEventArgs(id, controlName, settings!));
            return;
        }
        if (!TryReadDraft(out var name, out var mirror, out var array, out var error)) { ShowError(error); return; }
        ShowError("");
        ApplyRequested?.Invoke(this, new ModifierPropertiesEventArgs(id, name, _enabled.Checked == true, mirror, array));
    }

    internal void RequestPlane()
    {
        if (Enabled && _nodeId is { } id && _kind == TreeNodeKind.Mirror)
            SetPlaneRequested?.Invoke(this, new ModifierPlaneEventArgs(id));
    }

    internal void RequestAxis(int axisIndex, bool reset)
    {
        if (Enabled && _nodeId is { } id && _kind == TreeNodeKind.Array && axisIndex is >= 0 and <= 2)
            SetAxisRequested?.Invoke(this, new ModifierAxisEventArgs(id, axisIndex, reset));
    }

    internal void RequestAddControlBox()
    {
        if (Enabled && _nodeId is { } id && _kind == TreeNodeKind.Bend)
            AddControlBoxRequested?.Invoke(this, new ModifierControlBoxEventArgs(id));
    }

    internal void RequestFitControlBox()
    {
        if (Enabled && _nodeId is { } id && _kind == TreeNodeKind.ControlBox)
            FitControlBoxRequested?.Invoke(this, new ModifierControlBoxEventArgs(id));
    }

    internal bool TryReadControlBoxDraft(out string name, out ControlBoxSettings? settings, out string error)
    {
        settings = null;
        if (!ModifierNames.TryNormalize(_name.Text, out name, out error)) return false;
        if (!double.TryParse(_strength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var strength) || !double.IsFinite(strength))
        { error = "Strength: enter a finite number of degrees."; return false; }
        if (_bendMode.SelectedIndex is not (0 or 1))
        { error = "Choose Limited or Unlimited mode."; return false; }
        settings = new ControlBoxSettings(strength, _bendMode.SelectedIndex == 0);
        return ModifierSettings.TryValidate(settings, out error);
    }

    internal bool TryReadDraft(out string name, out MirrorSettings? mirror, out ArraySettings? array, out string error)
    {
        mirror = null;
        array = null;
        if (!ModifierNames.TryNormalize(_name.Text, out name, out error)) return false;
        if (_kind == TreeNodeKind.Mirror)
        {
            mirror = (_loadedMirror ?? MirrorSettings.Default) with { KeepOriginal = _keepOriginal.Checked == true, Union = _union.Checked == true };
            if (!ModifierSettings.TryValidate(mirror, out error)) return false;
        }
        else if (_kind == TreeNodeKind.Array)
        {
            if (!_counts.TryRead(out var count, out error) || !_spacing.TryRead(out var spacing, out error)) return false;
            if (new[] { count.X, count.Y, count.Z }.Any(value => value < 1 || value > ModifierSettings.MaxArrayInstances || value != Math.Truncate(value)))
            {
                error = $"Each array count must be a whole number between 1 and {ModifierSettings.MaxArrayInstances}.";
                return false;
            }
            array = (_loadedArray ?? ArraySettings.Default) with { CountX = (int)count.X, CountY = (int)count.Y, CountZ = (int)count.Z, Spacing = spacing };
            if (!ModifierSettings.TryValidate(array, out error)) return false;
        }
        error = "";
        return true;
    }

    private static Label Label(string text) => new() { Text = text, Width = 1, Wrap = WrapMode.Word };
    private static StackLayout Stack(params Control[] controls)
    {
        var stack = new StackLayout { Orientation = Orientation.Vertical, HorizontalContentAlignment = HorizontalAlignment.Stretch, Spacing = 4 };
        foreach (var control in controls) stack.Items.Add(control);
        return stack;
    }

    private sealed class VectorFields : Panel
    {
        private readonly string _caption;
        private readonly TextBox[] _fields = [new() { Width = 1 }, new() { Width = 1 }, new() { Width = 1 }];

        public VectorFields(string caption, string id)
        {
            _caption = caption;
            var row = new TableRow();
            for (var i = 0; i < _fields.Length; i++)
            {
                var field = _fields[i];
                field.ID = id + "-" + "xyz"[i];
                field.ToolTip = caption + ": " + "XYZ"[i] + ". Use a decimal point for fractional values. Press Enter to apply.";
                row.Cells.Add(new TableCell(field, true));
            }
            Content = Stack(Label(caption), new TableLayout { Spacing = new Size(4, 0), Rows = { row } });
        }

        public void SubmitOnEnter(EventHandler<KeyEventArgs> handler)
        {
            foreach (var field in _fields) field.KeyDown += handler;
        }

        public void Set(ModifierVector vector)
        {
            _fields[0].Text = vector.X.ToString("G17", CultureInfo.InvariantCulture);
            _fields[1].Text = vector.Y.ToString("G17", CultureInfo.InvariantCulture);
            _fields[2].Text = vector.Z.ToString("G17", CultureInfo.InvariantCulture);
        }

        public bool TryRead(out ModifierVector vector, out string error)
        {
            var values = new double[3];
            for (var i = 0; i < values.Length; i++)
            {
                if (!double.TryParse(_fields[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                {
                    vector = default;
                    error = $"{_caption}: enter a finite number for {"XYZ"[i]}.";
                    return false;
                }
            }
            vector = new ModifierVector(values[0], values[1], values[2]);
            error = "";
            return true;
        }
    }
}

internal sealed class ModifierPropertiesEventArgs(Guid nodeId, string name, bool enabled, MirrorSettings? mirror, ArraySettings? array) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
    public string Name { get; } = name;
    public bool Enabled { get; } = enabled;
    public MirrorSettings? Mirror { get; } = mirror;
    public ArraySettings? Array { get; } = array;
}

internal sealed class ModifierEnabledEventArgs(Guid nodeId, bool enabled) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
    public bool Enabled { get; } = enabled;
}

internal sealed class ModifierPlaneEventArgs(Guid nodeId) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
}

internal sealed class ModifierAxisEventArgs(Guid nodeId, int axisIndex, bool reset) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
    public int AxisIndex { get; } = axisIndex;
    public bool Reset { get; } = reset;
}

internal sealed class ModifierControlBoxEventArgs(Guid nodeId) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
}

internal sealed class ControlBoxPropertiesEventArgs(Guid nodeId, string name, ControlBoxSettings settings) : EventArgs
{
    public Guid NodeId { get; } = nodeId;
    public string Name { get; } = name;
    public ControlBoxSettings Settings { get; } = settings;
}
