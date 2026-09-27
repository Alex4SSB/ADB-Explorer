using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPageContent
{
    private void ExplorerPageContent_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Alt/Ctrl combos arrive with empty or control-char Text, which would match every item.
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
            return;

        if (SearchBox.IsFocused || SearchBox.IsKeyboardFocusWithin
            || DetailsPaneControl.IsEditorFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || Instance.FileList.Actions.IsExplorerEditing)
            return;

        var selected = ExplorerList.ActiveSelectedItems.Count;
        var selectedIndex = ExplorerList.ActiveView.SelectedIndex;
        IBrowserItem? nextItem = null;

        for (int i = 0; i < ExplorerList.ActiveView.Items.Count; i++)
        {
            var item = (IBrowserItem)ExplorerList.ActiveView.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    ItemToSelect.Value = item;
                    break;
                }
                else
                    nextItem ??= item;
            }
        }

        if (selectedIndex == ExplorerList.ActiveView.SelectedIndex && nextItem is not null)
            ItemToSelect.Value = nextItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || SearchBox.IsKeyboardFocusWithin
            || NavigationBox.IsKeyboardFocusWithin
            || DetailsPaneControl.IsEditorFocused
            || Instance.FileList.Actions.IsExplorerEditing)
            return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && Instance.EffectiveDevice is null)
        {
            e.Handled = true;
            return;
        }

        bool handle = false;

        if (e.Key is Key.A && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ExplorerList.ToggleSelectAll();
            e.Handled = true;

            return;
        }
        
        if (e.Key is Key.Delete && Instance.FileList.Actions.DeleteEnabled)
        {
            FileActionLogic.DeleteFiles();
            e.Handled = true;
            return;
        }

        if (!NAVIGATION_KEYS.Contains(e.Key))
            return;

        if (Instance.FileList.Actions.IsExplorerVisible)
        {
            handle |= ExplorerList.ExplorerGridKeyNavigation(e.Key);
        }
        else if (Instance.FileList.Actions.IsDriveViewVisible)
        {
            handle |= ExplorerList.DriveViewKeyNavigation(e.Key);
        }

        e.Handled = handle;
    }

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        // Tunneling: run before UnselectAll so a nested MouseMove cannot start a rubber-band
        // from a stale down-point (item that was selected when the menu opened).
        if (ToolbarSubmenuDepth > 0 || SuppressSelectionAfterMenu)
        {
            SuppressSelectionAfterMenu = true;
            ExplorerList.CancelExplorerMarquee();
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        ExplorerList.EndExplorerMouseGesture();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left)
        {
            ExplorerList.EndExplorerMouseGesture();

            // After the cells had their say, which the flag is kept for.
            ExplorerList.ClearWasDraggingIfIdle();
        }

        if (Instance.FileList.Actions.ListingInProgress && e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
            return;
        }

        e.Handled = e.ChangedButton switch
        {
            MouseButton.XButton1 => NavHistory.NavigateBF(Navigation.SpecialLocation.Back),
            MouseButton.XButton2 => NavHistory.NavigateBF(Navigation.SpecialLocation.Forward),
            _ => false,
        };
    }

    private void MainWin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CopyPaste.IsDrag && CopyPaste.DragStatus is not CopyPasteService.DragState.Active && e.Key is Key.Escape)
            CopyPaste.DragBitmap = null;
        else
            OnButtonKeyDown(sender, e);
    }

    public void HandlePreviewKeyDown(KeyEventArgs e) => MainWin_PreviewKeyDown(this, e);

    public void HandlePreviewKeyUp(KeyEventArgs e) => MainWindow_OnPreviewKeyUp(this, e);

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            CopyPaste.DragBitmap = null;
    }
}
