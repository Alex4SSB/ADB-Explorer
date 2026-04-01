using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for FileIconView.xaml
/// </summary>
public partial class FileIconView : UserControl
{
    private bool _wasSelectedOnMouseDown;
    private bool _wasEditingOnMouseDown;
    private int _clickCount;

    public FileIconView()
    {
        InitializeComponent();
    }

    private void IconViewNameTextBlock_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (DataContext is not FileClass file)
            return;

        _wasSelectedOnMouseDown = file.IsSelected;
        _wasEditingOnMouseDown = file.IconViewModel.IsInEditMode;
        _clickCount = e.ClickCount;
    }

    private void IconViewNameTextBlock_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (DataContext is not FileClass file)
            return;

        if (!_wasSelectedOnMouseDown || _wasEditingOnMouseDown || _clickCount > 1)
            return;

        if (DevicesObject.Current.Root is not RootStatus.Enabled
            && file.Type is not (AbstractFile.FileType.File or AbstractFile.FileType.Folder))
            return;

        var path = file.FullPath;

        Task.Run(() =>
        {
            var start = DateTime.Now;

            while (true)
            {
                Task.Delay(100);

                if (DateTime.Now - start > RENAME_CLICK_DELAY)
                    break;

                var currentPath = App.AppDispatcher?.Invoke(() => (DataContext as FileClass)?.FullPath);
                if (_clickCount > 1 || currentPath != path)
                    return;
            }

            App.SafeInvoke(() =>
            {
                if (DataContext is FileClass currentFile && currentFile.FullPath == path && !currentFile.IconViewModel.IsInEditMode)
                {
                    currentFile.IconViewModel.IsInEditMode = true;
                    FileActions.IsExplorerEditing = true;
                }
            });
        });
    }

    private void IconViewNameEdit_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    private void IconViewNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        if (textBox.DataContext is not FileClass file || !file.IconViewModel.IsInEditMode)
            return;

        FileActionLogic.Rename(textBox);

        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
    }

    private void IconViewNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        if (textBox.DataContext is not FileClass file)
            return;

        if (e.Key is Key.Escape or Key.F2)
        {
            var name = FileHelper.DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
            {
                DirList.FileList.Remove(file);
            }
            else
            {
                textBox.Text = FileHelper.DisplayName(textBox);
            }

            file.IconViewModel.IsInEditMode = false;
            FileActions.IsExplorerEditing = false;
        }
        else if (e.Key is Key.Enter)
        {
            file.IconViewModel.IsInEditMode = false;
            FileActions.IsExplorerEditing = false;
        }
        else
            return;

        e.Handled = true;
    }

    private void IconViewNameEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        if (textBox.DataContext is not FileClass file)
            return;

        textBox.FilterString(CurrentDrive.IsFUSE
            ? INVALID_NTFS_CHARS
            : INVALID_UNIX_CHARS);

        FileActions.IsRenameUnixLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Unix);
        FileActions.IsRenameFuseLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.FUSE);
        FileActions.IsRenameWindowsLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Windows);
        FileActions.IsRenameDriveRootLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.WinRoot);

        var fullName = Settings.ShowExtensions
            ? textBox.Text
            : textBox.Text + file.Extension;

        var comparison = CurrentDrive.IsFUSE
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        FileActions.IsRenameUnique = !DirList.FileList.Except([file]).Any(f => f.FullName.Equals(fullName, comparison));
    }
}
