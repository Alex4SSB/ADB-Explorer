using ADB_Explorer.Models;
using static ADB_Explorer.Services.DialogService;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for AdbContentDialog.xaml
/// </summary>
public partial class AdbContentDialog : UserControl
{
    private static string GetIcon(DialogIcon icon) => icon switch
    {
        DialogIcon.None => "",
        DialogIcon.Critical => "\uEA39",
        DialogIcon.Exclamation => "\uE783",
        DialogIcon.Informational => "\uE946",
        DialogIcon.Tip => "\uE82F",
        DialogIcon.Delete => "\uE74D",
        _ => throw new NotImplementedException(),
    };

    public AdbContentDialog()
    {
        InitializeComponent();
    }

    public static AdbContentDialog StringDialog(string content, DialogIcon icon = DialogIcon.None, bool censorContent = true, bool copyToClipboard = false, string checkBoxContent = "")
    {
        var dialog = new AdbContentDialog();

        if (censorContent)
        {
            content = content.Replace(AdbExplorerConst.RECYCLE_PATH, Strings.Resources.S_DRIVE_TRASH);
        }

        if (copyToClipboard)
            Data.FileActions.MessageToCopy = content;

        dialog.Icon.Glyph = GetIcon(icon);
        dialog.ContentPresenter.Visibility = Visibility.Collapsed;
        dialog.DialogContent.Visibility = Visibility.Visible;
        dialog.DialogContent.Text = content;

        dialog.DialogContentCheckbox.Content = checkBoxContent;
        if (string.IsNullOrEmpty(checkBoxContent))
        {
            dialog.DialogContentCheckbox.Visibility = Visibility.Collapsed;
        }

        return dialog;
    }

    public static AdbContentDialog CustomContentDialog(UIElement content, DialogIcon icon = DialogIcon.None)
    {
        var dialog = new AdbContentDialog();

        dialog.Icon.Glyph = GetIcon(icon);
        dialog.ContentPresenter.Visibility = Visibility.Visible;
        dialog.DialogContent.Visibility = Visibility.Collapsed;
        dialog.ContentPresenter.Content = content;
        dialog.DialogContentCheckbox.Visibility = Visibility.Collapsed;

        return dialog;
    }

    public bool IsChecked => DialogContentCheckbox.IsChecked is true;
}
