using Eto.Forms;
using ModifierTree.Core;
using ModifierTree.Rhino.UI;

internal static class ModifierPropertiesChecks
{
    public static void Run()
    {
        var tree = new ModifierTreeModel();
        var mirror = tree.AddModifier(TreeNodeKind.Mirror);
        var array = tree.AddModifier(TreeNodeKind.Array);
        using var editor = new ModifierPropertiesEditor();
        var applied = new List<ModifierPropertiesEventArgs>();
        var enabled = new List<ModifierEnabledEventArgs>();
        var planes = new List<ModifierPlaneEventArgs>();
        var axes = new List<ModifierAxisEventArgs>();
        editor.ApplyRequested += (_, e) => applied.Add(e);
        editor.EnabledRequested += (_, e) => enabled.Add(e);
        editor.SetPlaneRequested += (_, e) => planes.Add(e);
        editor.SetAxisRequested += (_, e) => axes.Add(e);
        editor.Bind(mirror, true);
        TextBox Field(string id) => editor.FindChild<TextBox>(id);

        Check(editor.FindChild<CheckBox>("mirror-keep-original").Checked == true && editor.FindChild<CheckBox>("mirror-union").Checked == true,
            "New Mirror properties default to Keep original and Boolean Union");
        Check(editor.FindChild<Button>("modifier-apply").Visible, "Mirror retains its Apply button for checkbox drafts");
        Field("modifier-name").Text = "  Alternative  ";
        editor.FindChild<CheckBox>("mirror-union").Checked = false;
        editor.Bind(mirror, true);
        Check(applied.Count == 0 && editor.FindChild<CheckBox>("mirror-union").Checked == false && Field("modifier-name").Text == "  Alternative  ",
            "Properties preserve unapplied name and Mirror Union edits through a viewport refresh");

        editor.RequestPlane();
        editor.RequestAxis(0, false);
        Check(planes is [var planeRequest] && planeRequest.NodeId == mirror.Id && axes.Count == 0 && applied.Count == 0,
            "Set Plane requests point picking for the selected Mirror without applying unrelated drafts");

        editor.FindChild<CheckBox>("modifier-enabled").Checked = false;
        Check(enabled is [{ Enabled: false }] && enabled[0].NodeId == mirror.Id && applied.Count == 0,
            "Enabled checkbox emits an immediate request without applying the parameter draft");
        tree.SetModifierEnabled(mirror.Id, false, out _);
        var movedPlane = mirror.Mirror! with { Origin = new ModifierVector(20, 0, 0), Normal = new ModifierVector(0, 1, 0) };
        tree.SetMirrorSettings(mirror.Id, movedPlane, out _);
        editor.Bind(mirror, true);
        editor.ApplyDraft();
        Check(applied is [{ Name: "Alternative", Enabled: false, Mirror: { Origin.X: 20, Normal.Y: 1, Union: false } }] && applied[0].NodeId == mirror.Id,
            "Apply retains the newly set plane and unapplied options after an ON/OFF or plane refresh");

        tree.SetMirrorSettings(mirror.Id, movedPlane with { KeepOriginal = false, Union = false }, out _);
        editor.Bind(mirror, true);
        Check(editor.FindChild<CheckBox>("mirror-keep-original").Checked == false && editor.FindChild<CheckBox>("mirror-union").Checked == false,
            "Committed Mirror option changes replace the corresponding stale option draft");

        editor.Bind(array, true);
        Check(Field("modifier-name").Text == "" && Field("array-count-x").Text == "2", "Changing the selected Modifier loads its own settings");
        Check(!editor.FindChild<Button>("modifier-apply").Visible, "Array exposes Enter submission without an Apply button");
        Field("array-count-x").Text = "3";
        Field("array-count-y").Text = "2";
        Field("array-spacing-y").Text = "-4.5";
        editor.RequestPlane();
        editor.RequestAxis(0, false);
        editor.RequestAxis(1, true);
        editor.RequestAxis(3, false);
        Check(planes.Count == 1 && axes is [{ AxisIndex: 0, Reset: false }, { AxisIndex: 1, Reset: true }] && axes.All(request => request.NodeId == array.Id),
            "Array axis requests distinguish Set from Reset, retain the selected axis, and reject unrelated requests");

        var customAxes = array.Array! with { AxisX = new ModifierVector(0, 1, 0), AxisY = new ModifierVector(0.6, 0.8, 0) };
        tree.SetArraySettings(array.Id, customAxes, out _);
        editor.Bind(array, true);
        Check(Field("array-count-x").Text == "3" && Field("array-spacing-y").Text == "-4.5" &&
            editor.FindChild<Label>("array-axis-x").Text == "X axis: (0, 1, 0)",
            "Setting an axis updates its direction summary and preserves pending counts and spacing");
        var submitHandled = PressKey(Field("array-count-x"), Keys.Enter);
        Check(submitHandled && applied.Count == 2 && applied[1] is { Mirror: null, Array: { CountX: 3, CountY: 2, CountZ: 1, Spacing.Y: -4.5, AxisX.Y: 1, AxisY.X: 0.6 } },
            "Array Enter submits grid counts and signed spacing once, preserves axes, and consumes the key");
        Field("array-spacing-y").Text = "NaN";
        var invalidHandled = PressKey(Field("array-spacing-y"), Keys.Enter);
        Check(invalidHandled && applied.Count == 2, "Enter rejects non-finite spacing without passing the key to Rhino");
        Field("array-spacing-y").Text = "-4.5";
        Field("array-count-x").Text = "2.5";
        PressKey(Field("array-count-x"), Keys.Enter);
        Check(applied.Count == 2, "Fractional array counts are rejected before submission");
        Field("array-count-x").Text = "200";
        PressKey(Field("array-count-x"), Keys.Enter);
        Check(applied.Count == 2, "Array settings enforce the total placement limit before submission");

        editor.Bind(null, true);
        editor.ApplyDraft();
        editor.RequestAxis(0, false);
        editor.RequestPlane();
        Check(!editor.Visible && applied.Count == 2 && planes.Count == 1 && axes.Count == 2,
            "No single Modifier selection hides properties and prevents stale Apply and point-picking requests");
        editor.Bind(mirror, false);
        editor.ApplyDraft();
        editor.RequestPlane();
        editor.Bind(array, false);
        editor.RequestAxis(0, true);
        Check(!editor.Enabled && applied.Count == 2 && planes.Count == 1 && axes.Count == 2,
            "Read-only document state prevents property edits, point picking, and axis resets");
        PressKey(Field("array-count-x"), Keys.Enter);
        Check(applied.Count == 2, "Read-only Array ignores Enter submission");

        editor.Bind(null, true);
        editor.Bind(array, true);
        Check(!PressKey(Field("array-count-x"), Keys.Tab) && applied.Count == 2,
            "Tab keeps normal field navigation without applying Array drafts");
        var numericIds = new[] { "array-count-x", "array-count-y", "array-count-z", "array-spacing-x", "array-spacing-y", "array-spacing-z" };
        foreach (var id in numericIds)
        {
            var before = applied.Count;
            if (!PressKey(Field(id), Keys.Enter) || applied.Count != before + 1)
                throw new Exception("FAIL: Enter must submit exactly once from " + id);
        }
        Check(applied.Count == 8, "Every Count and Spacing axis field submits its current Array draft on Enter");
        Field("modifier-name").Text = "  New array  ";
        PressKey(Field("modifier-name"), Keys.Enter);
        Check(applied.Count == 9 && applied[^1].Name == "New array",
            "Array names remain editable with Enter after removing the Apply button");
    }

    private static bool PressKey(Control control, Keys key)
    {
        var args = new KeyEventArgs(key, KeyEventType.KeyDown, null);
        // Raise Eto's actual event, exercising the handlers attached to each TextBox.
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
