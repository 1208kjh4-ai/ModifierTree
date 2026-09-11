using Eto.Forms;
using ModifierTree.Rhino.UI;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;

internal static class TreeGridSelectionChecks
{
    public static void Run()
    {
        if (Application.Instance is null) _ = new Application(new Eto.Wpf.Platform());
        var first = new TreeGridItem("A");
        var second = new TreeGridItem("B");
        var branch = new TreeGridItem("Modifier") { Expanded = true };
        branch.Children.Add(first);
        branch.Children.Add(second);
        var hidden = new TreeGridItem("Hidden child");
        var collapsed = new TreeGridItem("Collapsed Modifier") { Expanded = false };
        collapsed.Children.Add(hidden);
        var last = new TreeGridItem("Last");
        using var tree = new TreeGridView { AllowMultipleSelection = true, Size = new Eto.Drawing.Size(400, 300) };
        tree.Columns.Add(new GridColumn { HeaderText = "Name", DataCell = new TextBoxCell(0), Width = 350 });
        tree.DataStore = new TreeGridItemCollection { branch, collapsed, last };
        tree.AttachNative();
        var native = (WpfControls.DataGrid)tree.ControlObject;
        native.Measure(new Wpf.Size(400, 300));
        native.Arrange(new Wpf.Rect(0, 0, 400, 300));
        native.UpdateLayout();

        Check(native.SelectionMode == WpfControls.DataGridSelectionMode.Extended,
            "Tree enables native extended selection for Shift ranges and Ctrl additions");

        TreeGridSelection.Restore(tree, [first, second]);
        Check(tree.SelectedItems.SequenceEqual(new object[] { first, second }) && tree.SelectedRows.SequenceEqual(new[] { 1, 2 }),
            "Tree restores multiple selected source rows together");

        TreeGridSelection.Restore(tree, [first]);
        var changes = 0;
        tree.SelectionChanged += (_, _) => changes++;
        var primary = tree.SelectedItem;
        native.SelectedItems.Add(second);
        Check(changes > 0 && ReferenceEquals(primary, tree.SelectedItem) && tree.SelectedItems.Count() == 2,
            "SelectionChanged reports added rows while the primary item stays selected");

        TreeGridSelection.Restore(tree, [first, last]);
        Check(tree.SelectedRows.SequenceEqual(new[] { 1, 4 }) && tree.SelectedItems.SequenceEqual(new object[] { first, last }),
            "Selection row indexes skip descendants of collapsed branches");

        TreeGridSelection.Restore(tree, [hidden, last]);
        Check(tree.SelectedItems.SequenceEqual(new object[] { last }) && !collapsed.Expanded,
            "Restoring a hidden descendant does not expand its parent or select an unrelated row");

        var replacementFirst = new TreeGridItem("A");
        var replacementSecond = new TreeGridItem("B");
        tree.DataStore = new TreeGridItemCollection { replacementFirst, replacementSecond };
        TreeGridSelection.Restore(tree, [replacementFirst, replacementSecond]);
        Check(tree.SelectedItems.SequenceEqual(new object[] { replacementFirst, replacementSecond }) &&
            !tree.SelectedItems.Contains(first) && !tree.SelectedItems.Contains(second),
            "Selection restores after rebuilding rows using current item references");

        TreeGridSelection.Restore(tree, []);
        Check(!tree.SelectedItems.Any(), "Empty selection restore clears all selected rows");

        tree.DataStore = new TreeGridItemCollection();
        TreeGridSelection.Restore(tree, [replacementFirst]);
        Check(!tree.SelectedItems.Any(), "Empty tree ignores selection references from a previous store");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
