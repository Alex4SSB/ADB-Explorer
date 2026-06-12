using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
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

    /// <summary>
    /// Raised when icon rename editing starts. Sender is the <see cref="FileIconView"/> instance.
    /// </summary>
    public static event EventHandler<System.Windows.Controls.TextBox> RenameStarted;

    /// <summary>
    /// Raised when icon rename editing ends. Sender is the <see cref="FileIconView"/> instance.
    /// </summary>
    public static event EventHandler RenameEnded;

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

        if (DataContext is FileClass)
            RenameStarted?.Invoke(this, textBox);
    }

    private static void ExitIconEditMode(FileClass file)
    {
        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        RenameEnded?.Invoke(null, EventArgs.Empty);
    }

    private void IconViewNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        if (textBox.DataContext is FileClass file && !file.IconViewModel.IsInEditMode)
            return;

        FileViewModelBase.RenameCommit(textBox, ExitIconEditMode);
    }

    private void IconViewNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        FileViewModelBase.RenameKeyDown(textBox, e.Key, ExitIconEditMode);
        if (e.Key is Key.Escape or Key.F2 or Key.Enter)
            e.Handled = true;
    }

    private void IconViewNameEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        FileViewModelBase.RenameTextChanged(textBox);
    }
}
