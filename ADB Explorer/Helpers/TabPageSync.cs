using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.Views.Pages;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Keeps the page the window shows (<see cref="Data.CurrentPage"/>) in step with the active tab's
/// current history entry, and records page navigations made from the pane into that history.
/// </summary>
internal static class TabPageSync
{
    private static bool _syncing;

    /// <summary>Shows the page the tab's current entry stands for - the Explorer page for a folder or drive.</summary>
    internal static void ShowTabPage(ExplorerInstance tab)
    {
        if (!ReferenceEquals(tab, Data.ActiveExplorerInstance))
            return;

        var page = tab.History.Current?.PageType ?? typeof(ExplorerPage);

        // A split view is hosted by the Explorer page; a pane showing a page does so inside itself.
        if (tab.SplitOwner is not null || tab.SplitInstance is not null)
            page = typeof(ExplorerPage);

        App.SafeInvoke(() =>
        {
            _syncing = true;

            try
            {
                Data.CurrentPage.Value = page;
            }
            finally
            {
                _syncing = false;
            }

            FileActionLogic.UpdateFileActions();
        });
    }

    /// <summary>A page was opened without going through history (a pane item, or code) - add it to the active tab's history.</summary>
    internal static void RecordPage(Type? page)
    {
        if (_syncing || AdbLocation.ForPage(page) is not { } location)
            return;

        var tab = Data.ActiveExplorerInstance;
        tab.History.Navigate(location);
        tab.NotifyTabChanged();
    }
}
