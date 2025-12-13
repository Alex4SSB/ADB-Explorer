using ADB_Explorer.Controls;
using ADB_Explorer.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;

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

    public static void ShowMessage(string content, string title = "", DialogIcon icon = DialogIcon.None, bool censorContent = true, bool copyToClipboard = false)
    {
        var contentDialog = AdbContentDialog.StringDialog(content, icon, censorContent, copyToClipboard);

        _ = ShowDialog(contentDialog, title).Result;
    }

    public static async void ShowContent(UIElement content, string title = "", DialogIcon icon = DialogIcon.None)
    {
        var contentDialog = AdbContentDialog.CustomContentDialog(content, icon);

        await ShowDialog(contentDialog, title);
    }

    public static async Task<ContentDialogResult> ShowDialog(object content,
                                                 string title,
                                                 string primaryText = "",
                                                 string secondaryText = "",
                                                 string? closeText = null)
    {
        closeText ??= Strings.Resources.S_BUTTON_OK;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            SecondaryButtonText = secondaryText,
            CloseButtonText = closeText,
            FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };

        return await App.Services
            .GetRequiredService<IContentDialogService>()
            .ShowAsync(dialog, CancellationToken.None);
    }

    public static async Task<(ContentDialogResult, bool)> ShowConfirmation(string content,
                                                                           string title = "",
                                                                           string? primaryText = null,
                                                                           string secondaryText = "",
                                                                           string? cancelText = null,
                                                                           string checkBoxText = "",
                                                                           DialogIcon icon = DialogIcon.None,
                                                                           bool censorContent = true,
                                                                           bool copyToClipboard = false)
    {
        var contentDialog = AdbContentDialog.StringDialog(content, icon, censorContent, copyToClipboard, checkBoxText);

        primaryText ??= Strings.Resources.S_BUTTON_YES;

        cancelText ??= Strings.Resources.S_CANCEL;

        var result = await ShowDialog(contentDialog,
                                      title,
                                      primaryText,
                                      secondaryText,
                                      cancelText);

        return (result, contentDialog.IsChecked);
    }
}
