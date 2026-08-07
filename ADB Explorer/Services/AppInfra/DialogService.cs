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

    /// <summary>
    /// Conflict dialog: Replace / Skip these files / Decide each (content buttons); Close = Cancel.
    /// </summary>
    public static async Task<Helpers.FileMergeHelper.ConflictResolution> ShowConflictResolution(
        string message,
        string title)
    {
        // Native dialogs (e.g. CommonOpenFileDialog) steal activation; the host needs focus.
        if (Application.Current?.MainWindow is Window mainWindow)
        {
            mainWindow.Activate();
            await mainWindow.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        }

        var panel = new ConflictResolutionPanel { Message = message };
        var host = AdbContentDialog.CustomContentDialog(panel, DialogIcon.None);

        var dialog = new ContentDialog
        {
            Title = CreateTitle(title, null),
            Content = host,
            PrimaryButtonText = "",
            SecondaryButtonText = "",
            CloseButtonText = Strings.Resources.S_CANCEL,
            MinWidth = 440,
            FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };

        panel.Attach(dialog);

        var result = await App.Services
            .GetRequiredService<IContentDialogService>()
            .ShowAsync(dialog, CancellationToken.None);

        if (result is ContentDialogResult.None || panel.Choice is null)
            return Helpers.FileMergeHelper.ConflictResolution.Cancel;

        return panel.Choice.Value;
    }

    /// <summary>
    /// Per-file Replace/Skip with source/destination size and date. Returns null if cancelled;
    /// otherwise names to replace (skip the rest of the conflict set).
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ShowPerFileConflictResolution(
        IEnumerable<Helpers.FileMergeHelper.ConflictComparisonInfo> comparisons,
        string title,
        string sourcePath,
        string destinationPath)
    {
        var panel = new ConflictPerFilePanel();
        panel.SetConflicts(comparisons, sourcePath, destinationPath);
        var host = AdbContentDialog.CustomContentDialog(panel);

        var dialog = new ContentDialog
        {
            Title = CreateTitle(title, null),
            Content = host,
            PrimaryButtonText = Strings.Resources.S_CONFIRM,
            SecondaryButtonText = "",
            CloseButtonText = Strings.Resources.S_CANCEL,
            MinWidth = 640,
            FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };

        var result = await App.Services
            .GetRequiredService<IContentDialogService>()
            .ShowAsync(dialog, CancellationToken.None);

        if (result is not ContentDialogResult.Primary)
            return null;

        return panel.GetNamesToReplace();
    }
}
