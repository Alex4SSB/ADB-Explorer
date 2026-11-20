namespace ADB_Explorer.Controls;

public class AdbDataGrid : Wpf.Ui.Controls.DataGrid
{
    public AdbDataGrid()
    {
        // The grid's own Copy takes Ctrl+C before the window's binding, and swallows it even when it can't run,
        // so the app's Copy is bound here as well, where it's found first
        ClipboardCopyMode = DataGridClipboardCopyMode.None;

        if (AppActions.List.FirstOrDefault(a => a.Name is FileAction.FileActionType.Copy)?.KeyBinding is { } copy)
            InputBindings.Add(new KeyBinding(copy.Command, copy.Key, copy.Modifiers));
    }

    /// <summary>Replaces the selection with <paramref name="items"/> as one selection change, not one per item.</summary>
    public void SelectOnly(IEnumerable<object> items)
    {
        BeginUpdateSelectedItems();

        try
        {
            SelectedItems.Clear();

            foreach (var item in items)
                SelectedItems.Add(item);
        }
        finally
        {
            EndUpdateSelectedItems();
        }
    }
}
