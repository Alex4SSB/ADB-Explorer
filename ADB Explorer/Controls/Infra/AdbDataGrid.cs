namespace ADB_Explorer.Controls;

public class AdbDataGrid : Wpf.Ui.Controls.DataGrid
{
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
