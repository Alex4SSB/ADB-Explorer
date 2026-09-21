using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

/// <summary>
/// Owns the Explorer's open tabs. <see cref="Data.ActiveExplorerInstance"/> always mirrors
/// <see cref="FocusedPane"/> (the active tab, or its split view's second pane), so every existing
/// Data.*/NavHistory static-forwarding call site keeps meaning "the currently focused pane".
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

    /// <summary>The pane with focus - the active tab's own instance, or its split view's second pane.</summary>
    [ObservableProperty]
    public partial ExplorerInstance? FocusedPane { get; set; }

    partial void OnActiveTabChanged(ExplorerInstance? value)
    {
        if (value is not null)
            FocusedPane = value.LastFocusedPane ?? value;
    }

    /// <summary>Raised before focus moves, while the previously focused pane is still the active one.</summary>
    public event Action<ExplorerInstance, ExplorerInstance>? PaneFocusChanging;

    partial void OnFocusedPaneChanging(ExplorerInstance? oldValue, ExplorerInstance? newValue)
    {
        if (oldValue is not null && newValue is not null)
            PaneFocusChanging?.Invoke(oldValue, newValue);
    }

    partial void OnFocusedPaneChanged(ExplorerInstance? value)
    {
        if (value is null)
            return;

        value.OwningTab.LastFocusedPane = value;
        Data.ActiveExplorerInstance = value;
    }

    /// <summary>Raised after a tab enters or leaves split view; carries the second pane that was removed, if any.</summary>
    public event Action<ExplorerInstance, ExplorerInstance?>? SplitChanged;

    /// <summary>Every open tab's instances: each tab's own, plus its split view's second pane.</summary>
    public IEnumerable<ExplorerInstance> AllInstances => Tabs.SelectMany(PanesOf);

    private static IEnumerable<ExplorerInstance> PanesOf(ExplorerInstance tab)
    {
        yield return tab;

        if (tab.SplitInstance is { } split)
            yield return split;
    }

    public bool OwnsInstance(ExplorerInstance instance) => AllInstances.Contains(instance);

    /// <summary>The pane that browsing actions target, lazily creating the first tab if none exists yet.</summary>
    public ExplorerInstance EnsureFocusedPane() => FocusedPane ?? EnsureActiveTab();

    /// <summary>Focuses <paramref name="pane"/>, switching to its tab first if that isn't the active one.</summary>
    public void FocusPane(ExplorerInstance pane)
    {
        var tab = pane.OwningTab;
        tab.LastFocusedPane = pane;

        if (!ReferenceEquals(ActiveTab, tab))
            ActiveTab = tab;
        else
            FocusedPane = pane;
    }

    /// <summary>Turns a split view from side by side to stacked, or back, without changing which pane is first.</summary>
    public void SwitchSplitOrientation(ExplorerInstance tab)
    {
        if (tab.SplitInstance is null)
            return;

        tab.IsSplitStacked = !tab.IsSplitStacked;
        SplitChanged?.Invoke(tab, null);
    }

    public void ToggleSplit(ExplorerInstance tab, bool stacked = false)
    {
        if (tab.SplitInstance is not null)
            CloseSplit(tab);
        else if (stacked)
            OpenSplit(tab, SplitSide.Bottom);
        else
            OpenSplit(tab);
    }

    /// <summary>Shows a second pane beside or above the tab's own, starting at the same location - on the right by default.</summary>
    public void OpenSplit(ExplorerInstance tab, SplitSide side = SplitSide.Right)
    {
        if (tab.SplitInstance is not null)
            return;

        var split = new ExplorerInstance
        {
            Device = tab.EffectiveDevice,
            TracksAppWideCurrentDevice = false,
        };

        var primary = tab;

        if (side.IsFirst())
        {
            // The new pane takes the tab's place in the list, and the tab's own pane becomes its second one.
            primary = split;

            MergingTabs.Add(tab);

            try
            {
                var index = Tabs.IndexOf(tab);
                Tabs.RemoveAt(index);
                Tabs.Insert(index, split);
            }
            finally
            {
                MergingTabs.Clear();
            }

            var wasActive = ReferenceEquals(ActiveTab, tab);

            tab.LastFocusedPane = null;
            tab.SplitOwner = split;
            split.SplitInstance = tab;

            if (wasActive)
                ActiveTab = split;
        }
        else
        {
            split.SplitOwner = tab;
            tab.SplitInstance = split;
        }

        primary.IsSplitStacked = side.IsStacked();

        SplitChanged?.Invoke(primary, null);

        FocusPane(split);
        NavigateFocusedPaneLike(tab);
    }

    /// <summary>Points the focused pane's header at the location <paramref name="source"/> shows, through the app-wide navigation signals that only the focused header reacts to.</summary>
    private static void NavigateFocusedPaneLike(ExplorerInstance source)
    {
        Data.RuntimeSettings.InitLister = true;

        if (source.FileList.Actions.IsDriveViewVisible)
            Data.RuntimeSettings.DriveViewNav = true;
        else if (!string.IsNullOrEmpty(source.FileList.Path))
            Data.RuntimeSettings.PathBoxNavigation = source.FileList.Path;
    }

    /// <summary>Tabs being folded into a split view: they leave the tab list but stay alive as panes.</summary>
    public HashSet<ExplorerInstance> MergingTabs { get; } = [];

    /// <summary>Whether <paramref name="dragged"/> can become the split view's other pane of <paramref name="target"/> - or, dragged onto its own tab, split it.</summary>
    public bool CanMerge(ExplorerInstance target, ExplorerInstance dragged)
    {
        if (!Tabs.Contains(target) || !Tabs.Contains(dragged) || target.SplitInstance is not null)
            return false;

        return ReferenceEquals(target, dragged) || dragged.SplitInstance is null;
    }

    /// <summary>Folds <paramref name="dragged"/> into <paramref name="target"/> as a split view, against the given edge.</summary>
    public void MergeTab(ExplorerInstance target, ExplorerInstance dragged, SplitSide draggedSide)
    {
        if (!CanMerge(target, dragged))
            return;

        var primary = target;
        var secondary = dragged;
        var draggedOnLeft = draggedSide.IsFirst();

        if (draggedOnLeft)
        {
            primary = dragged;
            secondary = target;
        }

        MergingTabs.Add(target);
        MergingTabs.Add(dragged);

        try
        {
            Tabs.Remove(dragged);

            if (draggedOnLeft)
            {
                var index = Tabs.IndexOf(target);
                Tabs.Remove(target);
                Tabs.Insert(index, dragged);
            }
        }
        finally
        {
            MergingTabs.Clear();
        }

        target.LastFocusedPane = null;
        dragged.LastFocusedPane = null;

        secondary.SplitOwner = primary;
        primary.SplitInstance = secondary;
        primary.IsSplitStacked = draggedSide.IsStacked();

        if (!ReferenceEquals(ActiveTab, primary))
            ActiveTab = primary;

        SplitChanged?.Invoke(primary, null);

        FocusPane(dragged);
    }

    /// <summary>Raised after a split tab's panes trade places, before the new primary pane becomes the active tab.</summary>
    public event Action<ExplorerInstance, ExplorerInstance>? PanesSwapped;

    /// <summary>Exchanges the two panes of a split tab: the second pane becomes the tab's own, taking its place in the list.</summary>
    public void SwapPanes(ExplorerInstance tab)
    {
        if (tab.SplitInstance is not { } split)
            return;

        var focused = FocusedPane;
        var wasActive = ReferenceEquals(ActiveTab, tab);

        MergingTabs.Add(tab);

        try
        {
            var index = Tabs.IndexOf(tab);
            Tabs.RemoveAt(index);
            Tabs.Insert(index, split);
        }
        finally
        {
            MergingTabs.Clear();
        }

        tab.SplitInstance = null;
        tab.SplitOwner = split;
        split.SplitOwner = null;
        split.SplitInstance = tab;
        split.IsSplitStacked = tab.IsSplitStacked;
        tab.IsSplitStacked = false;

        // The window restructures before the tab list's selection follows, so the focused pane is kept as it was.
        split.LastFocusedPane = focused;
        tab.LastFocusedPane = null;

        PanesSwapped?.Invoke(tab, split);

        if (wasActive)
            ActiveTab = split;
    }

    /// <summary>Closes one pane of a split tab; the other one stays as the tab.</summary>
    public void ClosePane(ExplorerInstance tab, bool closeFirst)
    {
        if (tab.SplitInstance is not { } split)
            return;

        if (!closeFirst)
        {
            CloseSplit(tab);
            return;
        }

        // Closing the first pane is closing the second one after they trade places.
        SwapPanes(tab);
        CloseSplit(split);
    }

    /// <summary>Removes the tab's second pane; the tab's own pane keeps the focus.</summary>
    public void CloseSplit(ExplorerInstance tab)
    {
        if (tab.SplitInstance is not { } split)
            return;

        if (ReferenceEquals(FocusedPane, split))
            FocusPane(tab);

        tab.LastFocusedPane = null;
        tab.SplitInstance = null;
        tab.IsSplitStacked = false;
        split.FileList.DirList?.Stop();

        SplitChanged?.Invoke(tab, split);
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

    /// <summary>Opens an app page (Settings, Devices...) in a tab of its own, or a new explorer tab for the Explorer page.</summary>
    public void OpenPageInNewTab(Type pageType)
    {
        if (pageType == typeof(Views.Pages.ExplorerPage))
        {
            AddTab();
            return;
        }

        if (AdbLocation.ForPage(pageType) is not { } location)
            return;

        var instance = new ExplorerInstance
        {
            Device = ActiveTab?.Device ?? Data.DevicesObject?.Current,
            TracksAppWideCurrentDevice = ActiveTab?.TracksAppWideCurrentDevice ?? true,
        };

        Tabs.Add(instance);
        ActiveTab = instance;

        instance.History.Navigate(location);
        instance.NotifyTabChanged();
    }

    /// <summary>Activates the tab <paramref name="offset"/> places from the active one, wrapping around the ends.</summary>
    public void CycleTab(int offset)
    {
        if (Tabs.Count < 2 || ActiveTab is null)
            return;

        var index = Tabs.IndexOf(ActiveTab);
        if (index < 0)
            return;

        var next = (index + offset) % Tabs.Count;
        if (next < 0)
            next += Tabs.Count;

        ActiveTab = Tabs[next];
    }

    /// <summary>The tabs listed before <paramref name="tab"/>.</summary>
    public List<ExplorerInstance> TabsBefore(ExplorerInstance tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index <= 0)
            return [];

        return [.. Tabs.Take(index)];
    }

    /// <summary>The tabs listed after <paramref name="tab"/>.</summary>
    public List<ExplorerInstance> TabsAfter(ExplorerInstance tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return [];

        return [.. Tabs.Skip(index + 1)];
    }

    /// <summary>Closes <paramref name="toClose"/>, leaving <paramref name="keep"/> as the active tab.</summary>
    public void CloseTabs(IEnumerable<ExplorerInstance> toClose, ExplorerInstance keep)
    {
        var closing = toClose.Where(t => !ReferenceEquals(t, keep)).ToList();

        if (closing.Contains(ActiveTab!))
            ActiveTab = keep;

        foreach (var tab in closing)
            CloseTab(tab);
    }

    /// <summary>The tab <paramref name="offset"/> places from <paramref name="tab"/> in the list, if there is one.</summary>
    public ExplorerInstance? NeighborOf(ExplorerInstance tab, int offset)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return null;

        var neighbor = index + offset;
        if (neighbor < 0 || neighbor >= Tabs.Count)
            return null;

        return Tabs[neighbor];
    }

    /// <summary>Whether the tab next to <paramref name="tab"/> can be folded into a split view with it.</summary>
    public bool CanMergeWithNeighbor(ExplorerInstance tab, int offset)
        => NeighborOf(tab, offset) is { } other && !ReferenceEquals(tab, other) && CanMerge(tab, other);

    /// <summary>Combines <paramref name="tab"/> and its neighbor into a side by side split view, keeping their order.</summary>
    public void MergeWithNeighbor(ExplorerInstance tab, int offset)
    {
        if (NeighborOf(tab, offset) is not { } other || !CanMerge(tab, other))
            return;

        var side = SplitSide.Right;
        if (offset < 0)
            side = SplitSide.Left;

        MergeTab(tab, other, side);
    }

    /// <summary>Adds a tab beside <paramref name="tab"/> at the same location, on the same device.</summary>
    public ExplorerInstance DuplicateTab(ExplorerInstance tab)
    {
        var instance = new ExplorerInstance
        {
            Device = tab.EffectiveDevice,
            TracksAppWideCurrentDevice = false,
        };

        Tabs.Insert(Tabs.IndexOf(tab) + 1, instance);
        ActiveTab = instance;

        if (tab.History.Current is { IsPage: true } page)
        {
            instance.History.Navigate(page);
            instance.NotifyTabChanged();
            return instance;
        }

        NavigateFocusedPaneLike(tab);

        return instance;
    }

    public void CloseTab(ExplorerInstance tab)
    {
        if (Tabs.Count <= 1)
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        tab.SplitInstance?.FileList.DirList?.Stop();
        Tabs.RemoveAt(index);

        if (ReferenceEquals(ActiveTab, tab))
            ActiveTab = Tabs[Math.Max(0, index - 1)];
    }
}
