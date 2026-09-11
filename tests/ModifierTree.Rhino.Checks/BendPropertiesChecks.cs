using Eto.Forms;
using ModifierTree.Core;
using ModifierTree.Rhino.UI;

internal static class BendPropertiesChecks
{
    public static void Run()
    {
        var tree = new ModifierTreeModel();
        var bend = tree.AddModifier(TreeNodeKind.Bend);
        var objectId = Guid.NewGuid();
        if (!tree.AddControlBox(bend.Id, objectId, out var error)) throw new Exception(error);
        var box = tree.FindSource(objectId)!;
        using var editor = new ModifierPropertiesEditor();
        var modifiers = new List<ModifierPropertiesEventArgs>();
        var controls = new List<ControlBoxPropertiesEventArgs>();
        var added = new List<ModifierControlBoxEventArgs>();
        var fitted = new List<ModifierControlBoxEventArgs>();
        var toggled = new List<ModifierEnabledEventArgs>();
        editor.ApplyRequested += (_, e) => modifiers.Add(e);
        editor.ControlBoxApplyRequested += (_, e) => controls.Add(e);
        editor.AddControlBoxRequested += (_, e) => added.Add(e);
        editor.FitControlBoxRequested += (_, e) => fitted.Add(e);
        editor.EnabledRequested += (_, e) => toggled.Add(e);
        var name = editor.FindChild<TextBox>("modifier-name");
        var strength = editor.FindChild<TextBox>("bend-strength");
        var mode = editor.FindChild<DropDown>("bend-mode");

        editor.Bind(bend, true);
        Check(editor.Visible && editor.FindChild<StackLayout>("bend-properties").Visible &&
            !editor.FindChild<StackLayout>("control-box-properties").Visible &&
            editor.FindChild<CheckBox>("modifier-enabled").Visible,
            "Bend shows its Add Control Box section and whole-Modifier Enabled setting");
        editor.RequestAddControlBox();
        editor.RequestFitControlBox();
        Check(added is [var add] && add.NodeId == bend.Id && fitted.Count == 0,
            "Bend requests a new Control Box only for the selected Bend");
        name.Text = "  Curved beam  ";
        PressKey(name, Keys.Enter);
        Check(modifiers is [{ Name: "Curved beam", Mirror: null, Array: null }] && modifiers[0].NodeId == bend.Id,
            "Bend name uses the existing Modifier property submission path");

        editor.Bind(box, true, "Main box");
        Check(editor.Visible && !editor.FindChild<StackLayout>("bend-properties").Visible &&
            editor.FindChild<StackLayout>("control-box-properties").Visible && name.Text == "Main box" &&
            strength.Text == "45" && mode.SelectedIndex == 0 && !editor.FindChild<Button>("modifier-apply").Visible &&
            !editor.FindChild<CheckBox>("modifier-enabled").Visible && controls.Count == 0,
            "Control Box loads its native name, 45-degree Limited defaults, and Enter-based fields without firing an edit");
        name.Text = "  Lower bend  ";
        strength.Text = "-60";
        editor.Bind(box, true, "Main box");
        Check(name.Text == "  Lower bend  " && strength.Text == "-60" && controls.Count == 0,
            "Viewport refresh preserves pending Control Box name and Strength edits");
        editor.RequestFitControlBox();
        editor.RequestAddControlBox();
        Check(fitted is [var fit] && fit.NodeId == box.Id && added.Count == 1 && controls.Count == 0,
            "Fit to inputs targets the selected Control Box without submitting pending settings");
        Check(PressKey(strength, Keys.Enter) && controls is [{ Name: "Lower bend", Settings.Strength: -60, Settings.Limited: true }] &&
            controls[0].NodeId == box.Id && modifiers.Count == 1,
            "Strength Enter submits Control Box settings once and consumes the key before Rhino");
        mode.SelectedIndex = 1;
        Check(controls.Count == 2 && !controls[^1].Settings.Limited && controls[^1].Settings.Strength == -60,
            "Choosing Unlimited immediately submits the current Control Box draft");

        strength.Text = "NaN";
        Check(PressKey(strength, Keys.Enter) && controls.Count == 2,
            "Control Box rejects non-finite Strength while still consuming Enter");
        strength.Text = "181";
        PressKey(strength, Keys.Enter);
        Check(controls.Count == 2, "Control Box rejects Strength outside the supported bend range");
        strength.Text = "0";
        name.Text = "Neutral";
        Check(!PressKey(strength, Keys.Tab) && controls.Count == 2,
            "Control Box Tab navigation does not apply a draft");
        PressKey(name, Keys.Enter);
        Check(controls.Count == 3 && controls[^1] is { Name: "Neutral", Settings.Strength: 0, Settings.Limited: false },
            "Control Box name Enter also accepts a zero-degree bend");

        tree.SetControlBoxSettings(box.Id, new ControlBoxSettings(90, false), out _);
        editor.Bind(box, true, "Renamed in Rhino");
        Check(strength.Text == "90" && name.Text == "Renamed in Rhino" && mode.SelectedIndex == 1 && controls.Count == 3,
            "Committed Control Box settings and native name updates replace stale drafts without applying again");
        editor.Bind(box, false, "Renamed in Rhino");
        PressKey(strength, Keys.Enter);
        editor.RequestFitControlBox();
        mode.SelectedIndex = 0;
        Check(controls.Count == 3 && fitted.Count == 1,
            "Read-only state blocks Control Box Enter, mode edits, and fitting");
        editor.Bind(bend, false);
        editor.RequestAddControlBox();
        Check(added.Count == 1, "Read-only state blocks adding a Control Box");
        editor.Bind(null, true);
        editor.ApplyDraft();
        editor.RequestAddControlBox();
        editor.RequestFitControlBox();
        Check(!editor.Visible && controls.Count == 3 && modifiers.Count == 1 && added.Count == 1 && fitted.Count == 1 && toggled.Count == 0,
            "Clearing selection prevents stale Control Box actions and emits no Enabled requests");
    }

    private static bool PressKey(Control control, Keys key)
    {
        var args = new KeyEventArgs(key, KeyEventType.KeyDown, null);
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(control, [args]);
        return args.Handled;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
