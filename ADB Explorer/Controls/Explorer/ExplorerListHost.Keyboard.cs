using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    internal bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            switch (key)
            {
                case Key.Left or Key.Up:
                    DriveList.SelectedIndex = DriveList.Items.Count - 1;
                    break;

                case Key.Right or Key.Down:
                    DriveList.SelectedIndex = 0;
                    break;

                default:
                    return false;
            }

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        switch (key)
        {
            case Key.Enter:
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
                return true;

            case Key.Escape:
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;

            default:
                return false;
        }
    }

    internal bool ExplorerGridKeyNavigation(Key key)
    {
        if (ActiveView.Items.Count < 1 || Owner.DetailsPaneControl.IsEditorFocused)
            return false;

        switch (key)
        {
            case Key.Escape:
                if (SelectionRect.IsActive)
                    return true;

                ActiveUnselectAll();
                break;

            case Key.Left or Key.Right when !Owner.Instance.IsIconView:
                return false;

            case Key.Down or Key.Up or Key.Left or Key.Right or Key.Home or Key.End:
                if (Owner.BfNavigation)
                {
                    Owner.Instance.CurrentSelectedIndex = ActiveView.SelectedIndex;
                    Owner.BfNavigation = false;
                }

                if (Owner.Instance.IsIconView)
                {
                    var navKey = key;
                    if (RuntimeSettings.IsRTL && navKey is Key.Left or Key.Right)
                        navKey = navKey == Key.Left ? Key.Right : Key.Left;

                    var step = navKey is Key.Left or Key.Right ? 1 : IconView.ItemsPerRow;

                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        IconView.MultiSelect(navKey, step, Owner.Instance);
                    else
                        IconView.SingleSelect(navKey, step, Owner.Instance);
                }
                else
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        ActiveDataGrid.MultiSelect(key, Owner.Instance);
                    else
                        ActiveDataGrid.SingleSelect(key, Owner.Instance);
                }
                break;

            case Key.Enter:
                // Shift+Enter is bound to FollowLink on the main window; do not swallow it here.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    return false;

                if (ActiveSelectedItems.Count < 1 || IsInEditMode)
                    return false;

                if (ActiveSelectedItems.Count == 1
                    && ActiveView.SelectedItem is FileClass selected
                    && FileActionLogic.CanEnterSelection(selected))
                    DoubleClick(ActiveView.SelectedItem);
                return true;

            case Key.Apps:
                ActiveView.ContextMenu.IsOpen = true;
                break;

            default:
                return false;
        }

        return true;
    }

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
        var grid = (DataGrid)ItemsControl.ItemsControlFromItemContainer((DependencyObject)sender);

        var key = e.Key;
        switch (key)
        {
            case Key.Enter when IsInEditMode:
                return;
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                return;
            case Key.Enter:
                {
                    if (grid.SelectedItems.Count == 1
                        && grid.SelectedItem is FileClass selected
                        && FileActionLogic.CanEnterSelection(selected))
                        DoubleClick(grid.SelectedItem);
                    break;
                }
            case Key.Back:
                NavHistory.NavigateBF(Navigation.SpecialLocation.Back);
                break;

            case Key.Delete when FileActions.DeleteEnabled:
                FileActionLogic.DeleteFiles();
                break;

            case Key.Up or Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                grid.MultiSelect(key, Owner.Instance);
                break;

            case Key.Up or Key.Down:
                grid.SingleSelect(key, Owner.Instance);
                break;

            case Key.F2:
                if (FileActions.RenameEnabled)
                    AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}
