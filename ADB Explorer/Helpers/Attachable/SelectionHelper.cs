using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Helpers;

public static class SelectionHelper
{
    public static void MultiSelect(this DataGrid dataGrid, Key key, ExplorerViewModel vm)
    {
        vm.SelectionInProgress = true;

        var firstIndex = vm.FirstSelectedIndex;
        var currentIndex = vm.CurrentSelectedIndex;

        if (key == Key.Up)
            currentIndex--;
        else if (key == Key.Down)
            currentIndex++;
        else if (key == Key.Home)
            currentIndex = 0;
        else if (key == Key.End)
            currentIndex = dataGrid.Items.Count - 1;

        dataGrid.UnselectAll();

        var index1 = firstIndex < currentIndex ? firstIndex : currentIndex;
        var index2 = firstIndex < currentIndex ? currentIndex : firstIndex;

        for (int i = index1; i <= index2; i++)
        {
            if (i < 0 || i >= dataGrid.Items.Count)
                continue;

            dataGrid.SelectedItems.Add(dataGrid.Items[i]);
        }

        if (currentIndex >= 0 && currentIndex < dataGrid.Items.Count)
            dataGrid.ScrollIntoView(dataGrid.Items[currentIndex]);

        if (currentIndex >= 0 && currentIndex < dataGrid.Items.Count)
            vm.CurrentSelectedIndex = currentIndex;

        vm.SelectionInProgress = false;
    }

    public static void SingleSelect(this DataGrid dataGrid, Key key, ExplorerViewModel vm)
    {
        if (dataGrid.Items.Count == 1 && dataGrid.SelectedIndex == -1)
        {
            dataGrid.SelectedIndex = 0;
            return;
        }

        dataGrid.SelectedIndex = vm.CurrentSelectedIndex;

        if (key == Key.Up)
        {
            if (dataGrid.SelectedIndex > -1)
                dataGrid.SelectedIndex--;
            else
                dataGrid.SelectedIndex = dataGrid.Items.Count - 1;
        }
        else if (key == Key.Down)
        {
            if (dataGrid.SelectedIndex < 0 || dataGrid.Items.IndexOf(dataGrid.SelectedItems[^1]) < dataGrid.Items.Count - 1)
                dataGrid.SelectedIndex++;
            else
                dataGrid.SelectedIndex = -1;
        }
        else if (key == Key.Home)
        {
            dataGrid.SelectedIndex = 0;
        }
        else if (key == Key.End)
        {
            dataGrid.SelectedIndex = dataGrid.Items.Count - 1;
        }

        vm.CurrentSelectedIndex = dataGrid.SelectedIndex;
        vm.FirstSelectedIndex = dataGrid.SelectedIndex;
        if (dataGrid.SelectedIndex > -1)
            dataGrid.ScrollIntoView(dataGrid.SelectedItem);
    }

    public static System.Windows.Controls.ListViewItem GetListViewItemContainer(System.Windows.Controls.ListView listView, int index = -1) =>
        listView.ItemContainerGenerator.ContainerFromIndex(index < 0 ? listView.SelectedIndex : index) as System.Windows.Controls.ListViewItem;

    public static void SingleSelect(this ListView listView, Key key, int step, ExplorerViewModel vm)
    {
        if (listView.Items.Count == 0)
            return;

        if (listView.Items.Count == 1 && listView.SelectedIndex == -1)
        {
            listView.SelectedIndex = 0;
            vm.CurrentSelectedIndex = 0;
            vm.FirstSelectedIndex = 0;
            return;
        }

        listView.SelectedIndex = vm.CurrentSelectedIndex;

        if (key is Key.Up or Key.Left)
        {
            if (listView.SelectedIndex > -1)
                listView.SelectedIndex = Math.Clamp(listView.SelectedIndex - step, -1, listView.Items.Count);
            else
                listView.SelectedIndex = listView.Items.Count - 1;
        }
        else if (key is Key.Down or Key.Right)
        {
            if (listView.SelectedIndex < 0 || listView.SelectedIndex < listView.Items.Count - 1)
                listView.SelectedIndex = Math.Min(listView.SelectedIndex + step, listView.Items.Count - 1);
            else
                listView.SelectedIndex = -1;
        }
        else if (key == Key.Home)
        {
            listView.SelectedIndex = 0;
        }
        else if (key == Key.End)
        {
            listView.SelectedIndex = listView.Items.Count - 1;
        }

        vm.CurrentSelectedIndex = listView.SelectedIndex;
        vm.FirstSelectedIndex = listView.SelectedIndex;
        if (listView.SelectedIndex > -1)
            listView.ScrollIntoView(listView.Items[listView.SelectedIndex]);
    }

    public static void MultiSelect(this ListView listView, Key key, int step, ExplorerViewModel vm)
    {
        var firstIndex = vm.FirstSelectedIndex;
        var currentIndex = vm.CurrentSelectedIndex;

        if (key is Key.Up or Key.Left)
            currentIndex = Math.Max(0, currentIndex - step);
        else if (key is Key.Down or Key.Right)
            currentIndex = Math.Min(listView.Items.Count - 1, currentIndex + step);
        else if (key == Key.Home)
            currentIndex = 0;
        else if (key == Key.End)
            currentIndex = listView.Items.Count - 1;

        listView.UnselectAll();

        var index1 = Math.Min(firstIndex, currentIndex);
        var index2 = Math.Max(firstIndex, currentIndex);

        for (int i = index1; i <= index2; i++)
        {
            if (i < 0 || i >= listView.Items.Count)
                continue;

            listView.SelectedItems.Add(listView.Items[i]);
        }

        if (currentIndex >= 0 && currentIndex < listView.Items.Count)
            listView.ScrollIntoView(listView.Items[currentIndex]);

        if (currentIndex >= 0 && currentIndex < listView.Items.Count)
            vm.CurrentSelectedIndex = currentIndex;
    }
}
