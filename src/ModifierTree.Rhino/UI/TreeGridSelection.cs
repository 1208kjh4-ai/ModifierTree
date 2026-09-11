using Eto.Forms;

namespace ModifierTree.Rhino.UI;

public static class TreeGridSelection
{
    /// <summary>Restore selection using rows in the current store, skipping collapsed descendants.</summary>
    public static void Restore(TreeGridView tree, IEnumerable<ITreeGridItem> selected)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(selected);
        var wanted = new HashSet<ITreeGridItem>(selected, ReferenceEqualityComparer.Instance);
        var selectedRows = new List<int>();
        var rowIndex = 0;

        void Visit(ITreeGridStore<ITreeGridItem> store)
        {
            for (var index = 0; index < store.Count; index++)
            {
                var item = store[index];
                if (wanted.Contains(item)) selectedRows.Add(rowIndex);
                rowIndex++;
                if (item.Expanded && item.Expandable && item is ITreeGridStore<ITreeGridItem> children)
                    Visit(children);
            }
        }

        if (tree.DataStore is { } store) Visit(store);
        // SelectedItems is read-only in Eto. SelectedRows applies the entire batch together.
        tree.SelectedRows = selectedRows;
    }
}
