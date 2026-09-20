using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

/// <summary>
/// Owns the Explorer's open tabs. <see cref="Data.ActiveExplorerInstance"/> always mirrors
/// <see cref="ActiveTab"/>, so every existing Data.*/NavHistory static-forwarding call site
/// keeps meaning "the currently focused tab" without further changes.
/// </summary>
public partial class ExplorerTabsViewModel : ObservableObject
{
    public ObservableList<ExplorerInstance> Tabs { get; } = [];

    /// <summary>Whether the open tabs are browsing more than one device.</summary>
    public bool SpansMultipleDevices => Tabs
        .Select(tab => tab.EffectiveDevice?.ID)
        .OfType<string>()
        .Distinct()
        .Skip(1)
        .Any();

    private readonly HashSet<ExplorerInstance> _watchedTabs = [];

    public ExplorerTabsViewModel()
    {
        Tabs.CollectionChanged += (_, _) => TabsChanged();
    }

    private void TabsChanged()
    {
        foreach (var gone in _watchedTabs.Except(Tabs).ToList())
        {
            gone.PropertyChanged -= Tab_PropertyChanged;
            _watchedTabs.Remove(gone);
        }

        foreach (var tab in Tabs)
        {
            if (_watchedTabs.Add(tab))
                tab.PropertyChanged += Tab_PropertyChanged;
        }

        RefreshTooltips();
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerInstance.EffectiveDevice))
            RefreshTooltips();
    }

    private void RefreshTooltips()
    {
        foreach (var tab in Tabs)
            tab.NotifyTooltipChanged();
    }

    [ObservableProperty]
    public partial ExplorerInstance? ActiveTab { get; set; }

    partial void OnActiveTabChanged(ExplorerInstance? value)
    {
        if (value is not null)
            Data.ActiveExplorerInstance = value;
    }

    /// <summary>Returns the active tab, lazily creating the very first one (from the app-wide
    /// default instance Phase 1 already wired everything to) if none exists yet - Explorer starts
    /// with no tabs until it's first navigated to.</summary>
    public ExplorerInstance EnsureActiveTab()
    {
        if (ActiveTab is null)
        {
            Tabs.Add(Data.ActiveExplorerInstance);
            ActiveTab = Data.ActiveExplorerInstance;
        }

        return ActiveTab;
    }

    /// <summary>Adds and activates a tab pinned to <paramref name="device"/>; the caller opens the device in it.</summary>
    public ExplorerInstance AddDeviceTab(LogicalDeviceViewModel device)
    {
        var instance = new ExplorerInstance
        {
            Device = device,
            TracksAppWideCurrentDevice = false,
        };

        Tabs.Add(instance);
        ActiveTab = instance;
        return instance;
    }

    public string NewTabTooltip => AppActions.Tooltip(FileAction.FileActionType.NewTab);

    public string CloseTabTooltip => AppActions.Tooltip(FileAction.FileActionType.CloseTab);

    public ExplorerInstance AddTab()
    {
        var mode = Data.Settings.NewTabLocation;
        var device = ActiveTab?.Device ?? Data.DevicesObject?.Current;

        var instance = new ExplorerInstance
        {
            Device = mode is AppSettings.NewTabLocationMode.NoLocation ? null : device,
            TracksAppWideCurrentDevice = mode is not AppSettings.NewTabLocationMode.DuplicateCurrentTab,
        };

        if (mode is AppSettings.NewTabLocationMode.DuplicateCurrentTab && ActiveTab is not null)
            instance.FileList.Path = ActiveTab.FileList.Path;

        Tabs.Add(instance);
        ActiveTab = instance;

        // ActiveTab is now this instance, so these (guarded) app-wide signals reach its header.
        Data.RuntimeSettings.InitLister = true;

        if (mode is AppSettings.NewTabLocationMode.DriveView)
            Data.RuntimeSettings.DriveViewNav = true;
        else if (mode is AppSettings.NewTabLocationMode.DuplicateCurrentTab && !string.IsNullOrEmpty(instance.FileList.Path))
            Data.RuntimeSettings.PathBoxNavigation = instance.FileList.Path;

        return instance;
    }

    public void CloseTab(ExplorerInstance tab)
    {
        if (Tabs.Count <= 1)
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        Tabs.RemoveAt(index);

        if (ReferenceEquals(ActiveTab, tab))
            ActiveTab = Tabs[Math.Max(0, index - 1)];
    }
}
