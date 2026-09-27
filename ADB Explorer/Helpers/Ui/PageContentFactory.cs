namespace ADB_Explorer.Helpers;

/// <summary>
/// Builds a second copy of an app page's content, for showing that page inside a split view's pane.
/// The pages themselves keep serving the window.
/// </summary>
internal static class PageContentFactory
{
    public static UserControl? Create(Type pageType)
    {
        UserControl? content = null;

        if (pageType == typeof(SettingsPage))
            content = new SettingsPageContent { DataContext = App.Services.GetService<SettingsViewModel>() };
        else if (pageType == typeof(DevicesPage))
            content = new DevicesPageContent { DataContext = App.Services.GetService<DevicesViewModel>() };
        else if (pageType == typeof(TerminalPage))
            content = new TerminalPageContent { DataContext = App.Services.GetService<TerminalViewModel>() };
        else if (pageType == typeof(LogPage))
            content = new LogPageContent { DataContext = App.Services.GetService<LogViewModel>() };
        else if (pageType == typeof(OperationsPage))
            content = new OperationsPageContent { DataContext = App.Services.GetService<OperationsViewModel>() };

        if (content is not null)
            RemovePageMargin(content);

        return content;
    }

    /// <summary>A pane is narrower than the window, so the margin a page keeps around its content is dropped.</summary>
    private static void RemovePageMargin(UserControl content)
    {
        if (content.Content is not FrameworkElement root)
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
