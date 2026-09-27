using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    private TextBox? _renameTextBox;

    internal bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive)
                return false;

            if (ActiveView.SelectedItem is not FileClass file)
                return false;

            var vm = Owner.Instance.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            return vm.IsInEditMode;
        }
        set
        {
            if (value && !FileActions.RenameEnabled)
                return;

            if (ActiveView.SelectedItem is not FileClass file)
                return;

            var vm = Owner.Instance.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            vm.IsInEditMode = value;
            FileActions.IsExplorerEditing = value;
        }
    }

    private void BeginRename(TextBox textBox) => _renameTextBox = textBox;

    private void ClearRename() => _renameTextBox = null;

    private void CommitRenameIfDeselected()
    {
        if (_renameTextBox?.DataContext is not FileClass file)
        {
            ClearRename();
            return;
        }

        var vm = Owner.Instance.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
        if (!vm.IsInEditMode)
        {
            ClearRename();
            return;
        }

        if (ActiveSelectedItems.Count == 1 && ReferenceEquals(ActiveSelectedItems[0], file))
            return;

        RenameBox.FileCommit(_renameTextBox, Owner.Instance.IsIconView ? ExitIconEditMode : ExitFolderEditMode);
    }

    private void ExitFolderEditMode(FileClass file)
    {
        file.FolderViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void ExitIconEditMode(FileClass file)
    {
        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        RenameBox.FileKeyDown(textBox, e.Key, ExitFolderEditMode);
        if (e.Key is Key.Escape or Key.F2 or Key.Enter)
            e.Handled = true;
    }

    private void NameColumnEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        var textBox = sender as TextBox;

        if (textBox.DataContext is FileClass file)
        {
            RenameBox.PrepareFile(textBox);
            BeginRename(textBox);
            Owner.ShowRenameTooltip(textBox, file.FolderViewModel);
        }
    }

    private void IconView_RenameStarted(object? sender, TextBox textBox)
    {
        BeginRename(textBox);
        if (textBox.DataContext is FileClass file)
            Owner.ShowRenameTooltip(textBox, file.IconViewModel, centerHorizontally: true);
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (textBox.DataContext is FileClass file && !file.FolderViewModel.IsInEditMode)
            return;

        RenameBox.FileCommit(textBox, ExitFolderEditMode);
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        RenameBox.FileTextChanged(textBox);
    }
}
