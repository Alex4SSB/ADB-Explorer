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

    public static object CreateTitle(string title, DialogError? error) =>
        error is null ? title : new DialogTitle(title, error.Value);

    public static string FormatTitleString(string title, DialogError? error) =>
        error switch
        {
            null => title,
            _ when string.IsNullOrEmpty(title) => ((int)error).ToString(),
            _ => $"{title} ({(int)error})",
        };

    public static async void ShowMessage(string content,
                                       string title = "",
                                       DialogIcon icon = DialogIcon.None,
                                       bool censorContent = true,
                                       bool copyToClipboard = false,
                                       DialogError? error = null)
    {
        var contentDialog = AdbContentDialog.StringDialog(content, icon, censorContent, copyToClipboard);

        await ShowDialog(contentDialog, title, error: error);
    }

    public static async void ShowContent(UIElement content,
                                         string title = "",
                                         DialogIcon icon = DialogIcon.None,
                                         DialogError? error = null)
    {
        var contentDialog = AdbContentDialog.CustomContentDialog(content, icon);

        await ShowDialog(contentDialog, title, error: error);
    }

    public static async Task<ContentDialogResult> ShowDialog(object content,
                                                 string title,
                                                 string primaryText = "",
                                                 string secondaryText = "",
                                                 string? closeText = null,
                                                 DialogError? error = null)
    {
        closeText ??= Strings.Resources.S_BUTTON_OK;

        var dialog = new ContentDialog
        {
            Title = CreateTitle(title, error),
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
                                                                           bool copyToClipboard = false,
                                                                           DialogError? error = null)
    {
        var contentDialog = AdbContentDialog.StringDialog(content, icon, censorContent, copyToClipboard, checkBoxText);

        primaryText ??= Strings.Resources.S_BUTTON_YES;

        cancelText ??= Strings.Resources.S_CANCEL;

        var result = await ShowDialog(contentDialog,
                                      title,
                                      primaryText,
                                      secondaryText,
                                      cancelText,
                                      error);

        return (result, contentDialog.IsChecked);
    }
}
