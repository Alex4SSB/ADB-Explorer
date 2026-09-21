using ADB_Explorer.Controls.Pages;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.Views.Pages;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Builds the header control that makes up an app page's whole UI (the page itself is empty), for
/// showing a page inside a split view's pane. The window's own copies keep serving the window.
/// </summary>
internal static class PageHeaderFactory
{
    public static UserControl? Create(Type pageType)
    {
        UserControl? header = null;

        if (pageType == typeof(SettingsPage))
            header = new SettingsPageHeader { DataContext = App.Services.GetService<SettingsViewModel>() };
        else if (pageType == typeof(DevicesPage))
            header = new DevicesPageHeader { DataContext = App.Services.GetService<DevicesViewModel>() };
        else if (pageType == typeof(TerminalPage))
            header = new TerminalPageHeader { DataContext = App.Services.GetService<TerminalViewModel>() };
        else if (pageType == typeof(LogPage))
            header = new LogPageHeader { DataContext = App.Services.GetService<LogViewModel>() };
        else if (pageType == typeof(OperationsPage))
            header = new OperationsPageHeader { DataContext = App.Services.GetService<OperationsViewModel>() };

        if (header is not null)
            RemovePageMargin(header);

        return header;
    }

    /// <summary>A pane is narrower than the window, so the margin a page keeps around its content is dropped.</summary>
    private static void RemovePageMargin(UserControl header)
    {
        if (header.Content is not FrameworkElement root)
            return;

        if (root.Margin != default)
        {
            root.Margin = default;
            return;
        }

        if (root is Panel panel && panel.Children.OfType<Grid>().FirstOrDefault(grid => grid.Margin != default) is { } inner)
            inner.Margin = default;
    }
}
