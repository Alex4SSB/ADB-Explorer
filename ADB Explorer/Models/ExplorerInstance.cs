using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Models;

/// <summary>Which edge of a tab's page area a new split pane is placed against.</summary>
public enum SplitSide
{
    Left,
    Right,
    Top,
    Bottom,
}

public static class SplitSideExtensions
{
    /// <summary>The pane goes above or below the tab's own, rather than beside it.</summary>
    public static bool IsStacked(this SplitSide side) => side is SplitSide.Top or SplitSide.Bottom;

    /// <summary>The pane takes the tab's own place in the list, the left or top one.</summary>
    public static bool IsFirst(this SplitSide side) => side is SplitSide.Left or SplitSide.Top;
}

/// <summary>
/// Per-tab explorer state: the file-browsing session (<see cref="FileList"/>), its own
/// navigation history, and the view state a header UserControl binds to (sort, thumbnails,
/// view mode, selection). Exactly one instance is active app-wide for now, via <see cref="Data.ActiveExplorerInstance"/>.
/// </summary>
public partial class ExplorerInstance : ObservableObject
{
    public FileList FileList { get; } = new();

    public InstanceNavHistory History => field ??= new(this);

    /// <summary>True while this tab's current history entry is an app page (Settings, Devices...) rather than a folder / drive.</summary>
    public bool IsShowingPage => History.Current?.IsPage is true;

    public LogicalDeviceViewModel? Device
    {
        get;
        set
        {
            field = value;
            NotifyEffectiveDeviceChanged();
        }
    }

    /// <summary>This tab's own device once pinned to one, else the app-wide current device.</summary>
    public LogicalDeviceViewModel? EffectiveDevice => Device ?? Data.DevicesObject?.Current;

    /// <summary>Call when the app-wide current device changes under a tab that tracks it.</summary>
    public void NotifyEffectiveDeviceChanged()
    {
        OnPropertyChanged(nameof(EffectiveDevice));
        RefreshSavedItems();
        NotifyOwnerTabChanged();
    }

    /// <summary>Saved locations of this tab's own device - per tab, not shared, so switching
    /// tabs can never show another device's list.</summary>
    [ObservableProperty]
    public partial ObservableList<SavedLocation> SavedItems { get; set; } = [];

    public void RefreshSavedItems()
    {
        var deviceId = EffectiveDevice?.ID;
        if (Data.Settings.SavedLocations is not { } saved)
        {
            SavedItems = [];
            return;
        }

        SavedItems = [.. saved
            .Where(entry => entry.DeviceId == deviceId)
            .Select(entry => new SavedLocation(entry.Path, deviceId))];
    }

    /// <summary>Tab-strip label: this pane's own label, joined with the split view's second
    /// pane's label (" | ") while the tab is split.</summary>
    public string TabDisplayName
    {
        get
        {
            if (SplitInstance is not { } split)
                return OwnDisplayName;

            return $"{OwnDisplayName} | {split.OwnDisplayName}";
        }
    }

    /// <summary>This pane's label: whatever the nav tree would show for this same node - the device
    /// name for drive view, a drive's own name (Internal storage, SD card...) for its root, this
    /// tab's current folder name for a plain path, or a placeholder before it has navigated anywhere.</summary>
    private string OwnDisplayName
    {
        get
        {
            if (History.Current is not { } location)
                return EffectiveDevice?.Name ?? Strings.Resources.S_NEW_TAB;

            if (location.IsPage)
                return location.DisplayName;

            if (location.Location is Navigation.SpecialLocation.DriveView)
                return EffectiveDevice?.Name ?? location.GetHistoryName(EffectiveDevice);

            if (string.IsNullOrEmpty(location.Path))
                return location.GetHistoryName(EffectiveDevice);

            if (location.Path == AdbExplorerConst.RECYCLE_PATH)
                return AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Trash);

            if (EffectiveDevice?.Drives.FirstOrDefault(d => d.Path == location.Path) is { } drive)
                return drive.DisplayName;

            return NavigationTreeNode.FolderDisplayName(location.Path, EffectiveDevice?.ID);
        }
    }

    /// <summary>Tab-strip tooltip: both panes' paths (" | ") while the tab is split.</summary>
    public string TabTooltipPath
    {
        get
        {
            var own = OwnTooltipPath;
            if (SplitInstance is not { } split)
                return own;

            var other = split.OwnTooltipPath;
            if (own.Length == 0)
                return other;

            if (other.Length == 0)
                return own;

            return $"{own} | {other}";
        }
    }

    /// <summary>Full current path for the tab strip's tooltip - empty for a special location, whose
    /// short name is already everything there is to show.</summary>
    private string OwnTooltipPath
    {
        get
        {
            var path = History.Current?.Path ?? "";
            if (path == AdbExplorerConst.RECYCLE_PATH)
                path = AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Trash);

            if (path.Length == 0 || EffectiveDevice is not { } device)
                return path;

            // Which device a path belongs to only needs saying when tabs aren't all on one.
            return App.Services.GetService<ExplorerTabsViewModel>() is { SpansMultipleDevices: true }
                ? $"{device.Name} - {path}"
                : path;
        }
    }

    /// <summary>Call when whether the tabs span several devices may have changed.</summary>
    public void NotifyTooltipChanged() => OnPropertyChanged(nameof(TabTooltipPath));

    /// <summary>Tab-strip icon, resolved against this tab's own device rather than the app-wide
    /// current one. Drive roots and special locations use their own icon; a plain folder falls
    /// back to the same special-folder-aware icon the Explorer list itself uses.</summary>
    public BaseIcon? TabIcon
    {
        get
        {
            if (History.Current is not { } location)
                return new BaseIcon(FileToIconConverter.GetPhoneIcon(16), 16);

            if (location.GetIcon(EffectiveDevice) is { } locationIcon)
                return locationIcon;

            var bitmap = NavigationTreeNode.FolderIcon(location.Path, EffectiveDevice?.ID);
            return bitmap is null ? null : new BaseIcon(bitmap, 16);
        }
    }

    /// <summary>Call after navigating this tab's <see cref="History"/> so the tab strip refreshes -
    /// History itself isn't observable.</summary>
    public void NotifyTabChanged()
    {
        OnPropertyChanged(nameof(TabDisplayName));
        OnPropertyChanged(nameof(TabTooltipPath));
        OnPropertyChanged(nameof(TabIcon));
        OnPropertyChanged(nameof(IsShowingPage));

        NotifyOwnerTabChanged();
        TabPageSync.ShowTabPage(this);
    }

    /// <summary>A split view's second pane feeds its tab's merged label, so the tab refreshes too.</summary>
    private void NotifyOwnerTabChanged()
    {
        if (SplitOwner is not { } owner)
            return;

        owner.OnPropertyChanged(nameof(TabDisplayName));
        owner.OnPropertyChanged(nameof(TabTooltipPath));
    }

    /// <summary>The second pane shown beside this tab's own while it is in split view, else null.</summary>
    [ObservableProperty]
    public partial ExplorerInstance? SplitInstance { get; set; }

    partial void OnSplitInstanceChanged(ExplorerInstance? value)
    {
        OnPropertyChanged(nameof(TabDisplayName));
        OnPropertyChanged(nameof(TabTooltipPath));
    }

    /// <summary>Whether this pane's directory listing is still running past its short grace period, which the status bar marks with an asterisk.</summary>
    [ObservableProperty]
    public partial bool IsListingUnfinished { get; set; }

    /// <summary>Whether this tab's split view has its panes one above the other instead of side by side.</summary>
    public bool IsSplitStacked { get; set; }

    /// <summary>For a split view's second pane, the tab that hosts it; null for a tab's own instance.</summary>
    public ExplorerInstance? SplitOwner { get; set; }

    /// <summary>The tab this instance belongs to - itself, unless it is a split view's second pane.</summary>
    public ExplorerInstance OwningTab => SplitOwner ?? this;

    /// <summary>The pane last focused in this tab, restored when the tab is switched back to.</summary>
    public ExplorerInstance? LastFocusedPane { get; set; }

    /// <summary>True until this tab is explicitly pointed at a device - while true, it follows
    /// <see cref="Devices.Current"/> the way the single pre-tabs instance always did.</summary>
    public bool TracksAppWideCurrentDevice { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchExpanded { get; set; }

    [ObservableProperty]
    public partial ICollectionView ExplorerItemsSource { get; set; }

    [ObservableProperty]
    public partial IEnumerable<IBrowserItem> ExplorerSource { get; set; }

    [ObservableProperty]
    public partial ICollectionView DriveItemsSource { get; set; }

    [ObservableProperty]
    public partial ListSortDirection? SortDirection { get; set; }

    [ObservableProperty]
    public partial SortingSelector.SortingProperty? SortedColumn { get; set; }

    [ObservableProperty]
    public partial ListSortDirection? PackageTypeColumnSortDirection { get; set; }

    [ObservableProperty]
    public partial bool IsIconView { get; set; } = false;

    [ObservableProperty]
    public partial bool IsContentView { get; set; } = false;

    [ObservableProperty]
    public partial NavigationBox.ViewMode NavigationBoxMode { get; set; }

    [ObservableProperty]
    public partial bool IsSearchBoxFiltered { get; set; }

    [ObservableProperty]
    public partial ThumbnailService.ThumbnailSize CurrentThumbsSize { get; set; }

    public int FirstSelectedIndex { get; set; } = -1;

    public int CurrentSelectedIndex { get; set; } = -1;

    public int NextSelectedIndex { get; set; }

    public bool IsMenuOpen { get; set; }

    public bool SelectionInProgress { get; set; }

    /// <summary>Sets index to First, Current, and Next.</summary>
    public void SetIndexSingle(int value)
    {
        FirstSelectedIndex = value;
        CurrentSelectedIndex = value;
        NextSelectedIndex = value;
    }
}

/// <summary>
/// Per-tab back/forward history. Entries are folder / drive locations (stamped with their device)
/// or app pages, so one tab can walk through several devices and pages.
/// </summary>
public class InstanceNavHistory(ExplorerInstance owner)
{
    public List<AdbLocation> PathHistory { get; private set; } = [];

    public ObservableProperty<IEnumerable<SubMenu>> MenuHistory { get; private set; } = new() { Value = [] };

    /// <summary>The last folder / drive location this tab's explorer showed - it stays as is while a page is on top.</summary>
    public AdbLocation? LastExplorerLocation { get; private set; }

    /// <summary>Gives a location the tab's current device, unless it's a page or already has one.</summary>
    public AdbLocation Stamp(AdbLocation location)
    {
        if (location.IsPage || location.DeviceId is not null)
            return location;

        return location.WithDevice(owner.EffectiveDevice?.ID);
    }

    private static bool IsReachable(AdbLocation entry)
    {
        if (entry.IsPage || entry.DeviceId is null)
            return true;

        return Data.DevicesObject?.LogicalDeviceViewModels?.Any(device => device.ID == entry.DeviceId && device.Status is DeviceStatus.Ok) is true;
    }

    private void UpdateMenuHistory(LogicalDeviceViewModel? device)
    {
        var entries = PathHistory
            .Distinct()
            .Where(path => !path.Equals(Current))
            .ToList();

        var spansDevices = PathHistory
            .Select(path => path.DeviceId)
            .OfType<string>()
            .Distinct()
            .Skip(1)
            .Any();

        MenuHistory.Value = entries.Select(path => path.GetIconSubMenu(device, spansDevices));
    }

    /// <summary>Rebuilds history menu icons, e.g. after the recycle bin's empty/full state changes.</summary>
    public void RefreshMenuHistory(LogicalDeviceViewModel? device = null) => UpdateMenuHistory(device);

    private int historyIndex = -1;

    /// <summary>
    /// Device path left when navigating back; consumed to restore selection in the parent listing.
    /// </summary>
    private string? pendingSelectionPath;

    /// <summary>Returns and clears the path to select after a back-navigation completes.</summary>
    public string? TakePendingSelectionPath()
    {
        var path = pendingSelectionPath;
        pendingSelectionPath = null;
        return path;
    }

    /// <summary>Nearest entry in the given direction whose device is still connected, or -1.</summary>
    private int FindReachable(int step)
    {
        for (var i = historyIndex + step; i >= 0 && i < PathHistory.Count; i += step)
        {
            if (IsReachable(PathHistory[i]))
                return i;
        }

        return -1;
    }

    public bool BackAvailable => FindReachable(-1) >= 0;

    public bool ForwardAvailable => FindReachable(1) >= 0;

    public AdbLocation? GoBack(LogicalDeviceViewModel? device = null)
    {
        var target = FindReachable(-1);
        if (target < 0)
            return null;

        var departed = PathHistory[historyIndex].Path;
        pendingSelectionPath = string.IsNullOrEmpty(departed) ? null : departed;

        historyIndex = target;

        UpdateMenuHistory(device);

        return PathHistory[historyIndex];
    }

    public AdbLocation? GoForward(LogicalDeviceViewModel? device = null)
    {
        var target = FindReachable(1);
        if (target < 0)
            return null;

        pendingSelectionPath = null;

        historyIndex = target;

        UpdateMenuHistory(device);

        return PathHistory[historyIndex];
    }

    public AdbLocation? Current => PathHistory.Count > 0 ? PathHistory[historyIndex] : null;

    public void Navigate(string path, LogicalDeviceViewModel? device = null) => Navigate(new AdbLocation(path), device);

    public void Navigate(Navigation.SpecialLocation location, LogicalDeviceViewModel? device = null) => Navigate(new AdbLocation(location), device);

    /// <summary>For any non back / forward navigation.</summary>
    public void Navigate(AdbLocation path, LogicalDeviceViewModel? device = null)
    {
        path = Stamp(path);

        if (!path.IsPage)
            LastExplorerLocation = path;

        if (PathHistory.Count > 0 && path.Equals(PathHistory[historyIndex]))
        {
            return;
        }

        pendingSelectionPath = null;

        if (historyIndex < PathHistory.Count - 1)
        {
            PathHistory.RemoveRange(historyIndex + 1, PathHistory.Count - historyIndex - 1);
        }

        PathHistory.Add(path);
        historyIndex++;

        UpdateMenuHistory(device);
    }

    public void Reset()
    {
        PathHistory.Clear();
        historyIndex = -1;
        pendingSelectionPath = null;
        LastExplorerLocation = null;

        UpdateMenuHistory(null);
    }
}
