using Eto.Forms;
using ModifierTree.Core;
using ModifierTree.Rhino.UI;
using Wpf = System.Windows;

internal static class ManagerLayoutChecks
{
    public static void Run()
    {
        // Measure actual Eto/WPF controls without opening or interacting with a window.
        if (Application.Instance is null) _ = new Application(new Eto.Wpf.Platform());
        var tree = new TreeGridView { Size = new Eto.Drawing.Size(100, 100) };
        tree.Columns.Add(new GridColumn { HeaderText = "Name", DataCell = new TextBoxCell(0), Width = 140, MinWidth = 1 });
        tree.Columns.Add(new GridColumn { HeaderText = "State", DataCell = new TextBoxCell(1), Width = 70, MinWidth = 1 });
        tree.Columns.Add(new GridColumn { HeaderText = "InputWire", DataCell = new TextBoxCell(2), Width = 65, MinWidth = 1 });
        tree.DataStore = new TreeGridItemCollection { new TreeGridItem("A very long source name that must not stretch the panel", "Registered", "OFF") };
        var add = new Button { Text = "+ Add Object" };
        var modifier = new Button { Text = "+ Add Modifier" };
        var select = new Button { Text = "Select in Rhino" };
        var duplicate = new Button { Text = "Duplicate subtree" };
        var bake = new Button { Text = "Bake" };
        var merge = new Button { Text = "Merge" };
        var remove = new Button { Text = "Remove" };
        var drop = new Label { Text = "Drop here to move to root", Width = 1, Height = 28, Wrap = WrapMode.Word };
        var properties = new ModifierPropertiesEditor();
        var model = new ModifierTreeModel();
        properties.Bind(model.AddModifier(TreeNodeKind.Mirror), true);
        using var layout = ManagerLayout.Create(tree, add, modifier, drop, select, duplicate, bake, merge, remove,
            new CheckBox { Text = "Show result" }, new CheckBox { Text = "Show input wires" }, properties);
        layout.AttachNative();
        var element = (Wpf.FrameworkElement)layout.ControlObject;
        foreach (var (width, height) in new[] { (760, 700), (300, 700), (180, 700), (500, 700), (180, 400) })
        {
            element.Measure(new Wpf.Size(width, height));
            element.Arrange(new Wpf.Rect(0, 0, width, height));
            element.UpdateLayout();
            ManagerLayout.FitColumns(tree);
            element.UpdateLayout();
            var nativeControls = new[] { add, modifier, properties.FindChild<Button>("mirror-set-plane"), select, duplicate }.Select(control => (Wpf.FrameworkElement)control.ControlObject).ToArray();
            Check(nativeControls.Take(2).All(control => Math.Abs(control.ActualWidth - (width - 16)) < 2) &&
                nativeControls.Skip(2).All(control => control.ActualWidth <= width - 16 && control.ActualWidth >= width - 40),
                $"Buttons stretch within panel and scroll area at {width} x {height}: buttons={string.Join(",", nativeControls.Select(control => control.ActualWidth))}");
            var top = nativeControls.Select(control => control.TransformToAncestor(element).Transform(new Wpf.Point()).Y).ToArray();
            Check(top.Zip(top.Skip(1), (a, b) => b > a).All(value => value), $"Buttons remain vertically stacked at {width} x {height}");
            var actions = new[] { bake, merge, remove }.Select(control => (Wpf.FrameworkElement)control.ControlObject).ToArray();
            var positions = actions.Select(control => control.TransformToAncestor(element).Transform(new Wpf.Point())).ToArray();
            Check(actions.All(control => control.ActualWidth > 0 && Math.Abs(control.ActualWidth - actions[0].ActualWidth) < 2) &&
                positions.All(point => Math.Abs(point.Y - positions[0].Y) < 1) && positions[0].Y > top[^1] &&
                positions.Zip(positions.Skip(1), (a, b) => b.X > a.X).All(value => value) &&
                Math.Abs(positions[0].X - nativeControls[^1].TransformToAncestor(element).Transform(new Wpf.Point()).X) < 1 &&
                Math.Abs(positions[^1].X + actions[^1].ActualWidth - positions[0].X - nativeControls[^1].ActualWidth) < 2,
                $"Bake, Merge, and Remove share one row with equal widths at {width} x {height}: buttons={string.Join(",", actions.Select(control => control.ActualWidth))}");
            var nativeTree = (Wpf.FrameworkElement)tree.ControlObject;
            Check(Math.Abs(nativeTree.ActualWidth - (width - 16)) < 2 && tree.Columns.Sum(column => column.Width) <= nativeTree.ActualWidth,
                $"Tree and columns fit panel width {width}");
            var scroll = layout.FindChild<Scrollable>();
            Check(nativeTree.ActualHeight >= 100 && scroll.ScrollSize.Width <= scroll.Width + 1,
                $"Properties scroll vertically while the tree remains usable at {width} x {height}");
        }
        properties.Bind(model.AddModifier(TreeNodeKind.Array), true);
        element.Measure(new Wpf.Size(180, 400));
        element.Arrange(new Wpf.Rect(0, 0, 180, 400));
        element.UpdateLayout();
        Check(layout.FindChild<Scrollable>().ScrollSize.Width <= layout.FindChild<Scrollable>().Width + 1,
            "Array properties fit a narrow panel without horizontal scrolling");
        var axisButtons = new[] { "x", "y", "z" }.SelectMany(axis => new[] { "array-set-axis-" + axis, "array-reset-axis-" + axis })
            .Select(id => (Wpf.FrameworkElement)properties.FindChild<Button>(id).ControlObject).ToArray();
        Check(axisButtons.All(control => control.ActualWidth > 120 && control.ActualWidth <= 164),
            "Every Array Set Axis and Reset Axis button fits the 180-pixel panel");
        var axisButtonTops = axisButtons.Select(control => control.TransformToAncestor(element).Transform(new Wpf.Point()).Y).ToArray();
        Check(axisButtonTops.Zip(axisButtonTops.Skip(1), (a, b) => b > a).All(value => value),
            "Array axis controls remain vertically stacked in a narrow panel");
        ModifierPropertiesChecks.Run();
        BendPropertiesChecks.Run();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
