using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Views.Windows;

public partial class MainWindow
{
    /// <summary>
    /// Shows the page the active tab is on, and highlights its item. A split view leaves the navigation
    /// view sitting on a page its pane showed, where navigating to it again does nothing, so the window follows by hand.
    /// </summary>
    private void ShowCurrentPage()
    {
        if (!RootNavigation.IsLoaded || Data.CurrentPage.Value is not { } page)
            return;

        if (!Navigate(page) && PageFrame?.Content?.GetType() != page)
            RootNavigation.ReplaceContent(page);

        if (page == typeof(Pages.ExplorerPage))
            ShowExplorerContent(ExplorerTabs.EnsureActiveTab());
    }

    /// <summary>The Explorer page shows the content of whichever tab is active.</summary>
    private void ShowExplorerContent(ExplorerInstance tab)
        => App.Services.GetRequiredService<Pages.ExplorerPage>().ShowContent(GetOrCreateExplorerContent(tab));

    private Frame? PageFrame => RootNavigation.Template?.FindName("PART_NavigationViewContentPresenter", RootNavigation) as Frame;

    /// <summary>
    /// The page on its way into the frame. Its OnNavigatedTo can set <see cref="Data.CurrentPage"/>, and a navigation
    /// nested in that one breaks the frame (left empty, or on the wrong page), so it waits until this one is done.
    /// </summary>
    private object? _incomingPage;

    private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
    {
        _incomingPage = args.Page;
        Dispatcher.BeginInvoke(() => _incomingPage = null);

        if (args.Page is Pages.ExplorerPage)
            ShowExplorerContent(ExplorerTabs.EnsureActiveTab());
    }

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType)
    {
        if (_incomingPage is not null)
        {
            if (_incomingPage.GetType() != pageType)
                Dispatcher.BeginInvoke(() => Navigate(pageType));

            return true;
        }

        try
        {
            return RootNavigation.Navigate(pageType);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    INavigationView INavigationWindow.GetNavigation()
    {
        throw new NotImplementedException();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }
}
