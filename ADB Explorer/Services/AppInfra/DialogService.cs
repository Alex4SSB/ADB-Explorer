using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public static class DialogService
{
    public enum DialogIcon
    {
        None,
        Critical,
        Exclamation,
        Informational,
        Tip,
        Delete,
    }

    private static string Icon(DialogIcon icon) => icon switch
    {
        DialogIcon.None => "",
        DialogIcon.Critical => "\uEA39",
        DialogIcon.Exclamation => "\uE783",
        DialogIcon.Informational => "\uE946",
        DialogIcon.Tip => "\uE82F",
        DialogIcon.Delete => "\uE74D",
        _ => throw new NotImplementedException(),
    };

    private static readonly ContentDialog windowDialog = new();

    public static void ShowMessage(string content, string title = "", DialogIcon icon = DialogIcon.None, bool censorContent = true, bool hidePanes = true, bool copyToClipboard = false)
    {
        if (censorContent)
        {
            content = content.Replace(AdbExplorerConst.RECYCLE_PATH, "Recycle Bin");
        }

        if (copyToClipboard)
            Data.FileActions.MessageToCopy = content;

        ShowDialog(content, title, icon, hidePanes);
    }

    private static void HidePanes()
    {
        Data.RuntimeSettings.IsSettingsPaneOpen = 
        Data.RuntimeSettings.IsDevicesPaneOpen = false;
    }

    public static ContentDialog ShowDialog(object content, string title, DialogIcon icon = DialogIcon.None, bool hidePanes = true, string buttonText = null)
    {
        if (buttonText is null)
            buttonText = Strings.Resources.S_BUTTON_OK;

        windowDialog.Content = content;
        windowDialog.Title = title;
        windowDialog.PrimaryButtonText = null;
        windowDialog.CloseButtonText = buttonText;
        DialogHelper.SetDialogIcon(windowDialog, Icon(icon));

        if (hidePanes)
            HidePanes();

        windowDialog.ShowAsync();
        return windowDialog;
    }

    private static TextBlock DialogContentTextBlock;
    private static CheckBox DialogContentCheckbox;
    private static bool IsDialogChecked;
    private static SimpleStackPanel DialogContentStackPanel = new()
    {
        Spacing = 10
    };

    private static void InitContent(string textContent, string checkboxContent = "")
    {
        if (DialogContentTextBlock is null)
            DialogContentTextBlock = new();

        DialogContentTextBlock.Text = textContent;

        if (DialogContentCheckbox is null)
        {
            DialogContentCheckbox = new();
            DialogContentCheckbox.Checked += Checkbox_Checked;
            DialogContentCheckbox.Unchecked += Checkbox_Checked;
        }

        DialogContentCheckbox.Visibility = VisibilityHelper.Visible(!string.IsNullOrEmpty(checkboxContent));
        DialogContentCheckbox.Content = checkboxContent;
        DialogContentCheckbox.IsChecked = false;

        if (DialogContentStackPanel.Children.Count == 0)
        {
            DialogContentStackPanel.Children.Add(DialogContentTextBlock);
            DialogContentStackPanel.Children.Add(DialogContentCheckbox);
        }
    }

    public static async Task<(ContentDialogResult, bool)> ShowConfirmation(string content,
                                        string title = "",
                                        string primaryText = "Yes",
                                        string secondaryText = "",
                                        string cancelText = null,
                                        string checkBoxText = "",
                                        DialogIcon icon = DialogIcon.None,
                                        bool censorContent = true,
                                        bool hidePanes = true)
    {
        if (cancelText is null)
            cancelText = Strings.Resources.S_CANCEL;

        if (windowDialog.IsVisible)
        {
            return (ContentDialogResult.None, false);
        }

        if (censorContent)
        {
            content = content.Replace(AdbExplorerConst.RECYCLE_PATH, "Recycle Bin");
        }

        InitContent(content, checkBoxText);

        windowDialog.Content = DialogContentStackPanel;
        windowDialog.Title = title;
        windowDialog.PrimaryButtonText = primaryText;
        windowDialog.DefaultButton = ContentDialogButton.Primary;
        windowDialog.SecondaryButtonText = secondaryText;
        windowDialog.CloseButtonText = cancelText;
        DialogHelper.SetDialogIcon(windowDialog, Icon(icon));

        if (hidePanes)
            HidePanes();

        var result = await windowDialog.ShowAsync();

        return (result, IsDialogChecked);
    }

    private static void Checkbox_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        IsDialogChecked = DialogContentCheckbox.IsChecked.Value;
    }

    public static bool IsOpen
    {
        get => windowDialog.IsVisible;
        set => windowDialog.Hide();
    }
}
