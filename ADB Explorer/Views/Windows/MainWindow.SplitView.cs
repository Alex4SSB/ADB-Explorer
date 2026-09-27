namespace ADB_Explorer.Views.Windows;

public partial class MainWindow
{
    private void UnfocusNavigationRow()
    {
        if (_explorerContents.TryGetValue(Data.ActiveExplorerInstance, out var content))
            content.PathBoxFocus(false);

        Data.RaiseUnfocusSearchBox();
    }

    /// <summary>
    /// The active tab's explorer content listens for navigation signals, so it has to exist even while a page
    /// covers it - and its navigation row is what the window shows above the pane.
    /// </summary>
    private void EnsureActiveContent()
    {
        if (Data.DevicesObject is null)
            return;

        NavBarHost.Content = GetOrCreateExplorerContent(Data.ActiveExplorerInstance).NavBar;
    }

    /// <summary>The focused pane's navigation row replaces the shown one, and the tab's shared toolbar switches to it.</summary>
    private void OnPaneFocused(ExplorerInstance pane)
    {
        // The stray-selection guard is app-wide, so it follows the pane now in use, not the last one navigated.
        Data.RuntimeSettings.IsExplorerLoaded = pane.FileList.Actions.IsExplorerVisible;

        EnsureActiveContent();
        TabPageSync.ShowTabPage(pane);

        if (Data.DevicesObject is not null)
            GetOrCreateExplorerContent(pane.OwningTab).SetChromeInstance(pane);

        RefreshPaneVisuals(pane.OwningTab);
    }

    private void RefreshPaneVisuals(ExplorerInstance tab)
    {
        if (Data.DevicesObject is null)
            return;

        GetOrCreateExplorerContent(tab).RefreshPaneVisuals();

        if (tab.SplitInstance is { } split)
            GetOrCreateExplorerContent(split).RefreshPaneVisuals();
    }

    /// <summary>The split view's second pane becomes the tab's own content, and the old one is shown beside it instead.</summary>
    private void SwapSplitPanes(ExplorerInstance oldPrimary, ExplorerInstance newPrimary)
    {
        var oldContent = GetOrCreateExplorerContent(oldPrimary);
        var newContent = GetOrCreateExplorerContent(newPrimary);
        var (primarySize, secondarySize) = oldContent.PaneSizes;
        var stacked = oldContent.IsStacked;

        // A content control can only sit in one place, so each is freed before it is put in the other's.
        oldContent.HideSecondary();
        App.Services.GetRequiredService<Pages.ExplorerPage>().ShowContent(newContent);
        newContent.ClearPaneOnly();
        newContent.ShowSecondary(oldContent, stacked);
        newContent.SetPaneSizes(secondarySize, primarySize);

        newContent.SetChromeInstance(Data.ActiveExplorerInstance);
        RefreshPaneVisuals(newPrimary);
    }

    /// <summary>Puts a tab's split view's second pane beside its own, or takes it away again.</summary>
    private void ApplySplit(ExplorerInstance tab, ExplorerInstance? removed)
    {
        var content = GetOrCreateExplorerContent(tab);

        if (tab.SplitInstance is { } split)
        {
            content.ShowSecondary(GetOrCreateExplorerContent(split), tab.IsSplitStacked);
            RefreshPaneVisuals(tab);
            TabPageSync.ShowTabPage(Data.ActiveExplorerInstance);
            return;
        }

        content.HideSecondary();
        content.RefreshPaneVisuals();

        if (removed is not null)
            _explorerContents.Remove(removed);

        // Out of a split view, a tab showing a page is shown by the window again.
        TabPageSync.ShowTabPage(Data.ActiveExplorerInstance);
        ShowCurrentPage();
    }

    /// <summary>Until ADB is valid there's no real explorer content, so a disabled bar keeps the row - and the tab fusing into it - intact.</summary>
    private void ShowPlaceholderNavBar()
    {
        if (Data.DevicesObject is not null || NavBarHost.Content is not null || ExplorerTabs.ActiveTab is not { } tab)
            return;

        NavBarHost.Content = new Controls.ExplorerNavBar(tab) { IsEnabled = false };
    }

    private readonly Dictionary<ExplorerInstance, ExplorerPageContent> _explorerContents = [];

    /// <summary>One explorer content per tab, created lazily and cached for the tab's lifetime.</summary>
    internal ExplorerPageContent GetOrCreateExplorerContent(ExplorerInstance instance)
    {
        if (!_explorerContents.TryGetValue(instance, out var content))
        {
            content = new(App.Services.GetService<ExplorerViewModel>(), instance);
            _explorerContents[instance] = content;
        }

        return content;
    }
}
