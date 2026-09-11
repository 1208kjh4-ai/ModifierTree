using Eto.Drawing;
using Eto.Forms;

namespace ModifierTree.Rhino.UI;

internal static class ManagerLayout
{
    public static StackLayout Create(TreeGridView tree, Control addObject, Control addModifier, Control rootDrop,
        Control select, Control duplicate, Control bake, Control merge, Control remove, Control preview, Control inputVisible,
        Control properties)
    {
        tree.SizeChanged += (_, _) => FitColumns(tree);
        var resultActions = CreateResultActions(bake, merge, remove);
        var details = new StackLayout
        {
            Orientation = Orientation.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Spacing = 5,
            Items = { properties, select, duplicate, resultActions, preview, inputVisible }
        };
        var scroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Width = 1,
            Height = 240,
            Content = details
        };
        var layout = new StackLayout
        {
            Orientation = Orientation.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Padding(8), Spacing = 5,
            Items = { addObject, addModifier, new StackLayoutItem(tree, expand: true), rootDrop, scroll }
        };
        // Keep a usable tree while long parameter forms and actions scroll independently below it.
        layout.SizeChanged += (_, _) => scroll.Height = Math.Clamp((int)(layout.Height * 0.45), 100, 360);
        return layout;
    }

    private static PixelLayout CreateResultActions(params Control[] actions)
    {
        const int spacing = 5;
        var height = Math.Max(24, (int)Math.Ceiling(actions.Max(control => control.GetPreferredSize().Height)));
        var row = new PixelLayout { Width = 1, Height = height };
        foreach (var control in actions)
        {
            control.Size = new Size(1, height);
            row.Add(control, 0, 0);
        }
        // TableLayout adds each caption's preferred width before sharing extra space.
        // Divide the actual row width directly so all three actions remain equal.
        row.SizeChanged += (_, _) =>
        {
            var available = Math.Max(actions.Length, row.Width - spacing * (actions.Length - 1));
            for (var index = 0; index < actions.Length; index++)
            {
                var left = available * index / actions.Length;
                var right = available * (index + 1) / actions.Length;
                actions[index].Width = right - left;
                row.Move(actions[index], left + spacing * index, 0);
            }
        };
        return row;
    }

    public static void FitColumns(TreeGridView tree)
    {
        var width = Math.Max(3, tree.Width - 22);
        var state = Math.Min(82, Math.Max(1, (int)(width * 0.25)));
        var wires = Math.Min(70, Math.Max(1, (int)(width * 0.23)));
        tree.Columns[0].Width = Math.Max(1, width - state - wires);
        tree.Columns[1].Width = state;
        tree.Columns[2].Width = wires;
    }
}
