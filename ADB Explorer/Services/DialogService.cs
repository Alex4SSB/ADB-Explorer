using ADB_Explorer.Helpers;
using ModernWpf.Controls;
using System;
using System.Threading.Tasks;

namespace ADB_Explorer.Services
{
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

        public static void ShowMessage(string content, string title = "", DialogIcon icon = DialogIcon.None)
        {
            windowDialog.Content = content;
            windowDialog.Title = title;
            windowDialog.PrimaryButtonText = null;
            windowDialog.CloseButtonText = "Ok";
            TextHelper.SetAltText(windowDialog, Icon(icon));

            windowDialog.ShowAsync();
        }

        public static async Task<ContentDialogResult> ShowConfirmation(string content,
                                            string title = "",
                                            string primaryText = "Yes",
                                            string cancelText = "Cancel",
                                            DialogIcon icon = DialogIcon.None)
        {
            if (windowDialog.IsVisible)
            {
                return ContentDialogResult.None;
            }

            windowDialog.Content = content;
            windowDialog.Title = title;
            windowDialog.PrimaryButtonText = primaryText;
            windowDialog.DefaultButton = ContentDialogButton.Primary;
            windowDialog.CloseButtonText = cancelText;
            TextHelper.SetAltText(windowDialog, Icon(icon));

            return await windowDialog.ShowAsync();
        }
    }
}
