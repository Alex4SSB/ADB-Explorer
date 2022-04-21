using ADB_Explorer.Helpers;
using ModernWpf.Controls;
using System;

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
        }

        private static string Icon(DialogIcon icon) => icon switch
        {
            DialogIcon.None => "",
            DialogIcon.Critical => "\uEA39",
            DialogIcon.Exclamation => "\uE783",
            DialogIcon.Informational => "\uE946",
            DialogIcon.Tip => "\uE82F",
            _ => throw new NotImplementedException(),
        };

        private static readonly ContentDialog windowDialog = new();

        public static void ShowMessage(string content, string title = "", DialogIcon icon = DialogIcon.None)
        {
            windowDialog.Content = content;
            windowDialog.Title = title;
            windowDialog.CloseButtonText = "Ok";
            TextHelper.SetAltText(windowDialog, Icon(icon));

            windowDialog.ShowAsync();
        }
    }
}
