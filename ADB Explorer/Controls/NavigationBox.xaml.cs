using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NavigationBox.xaml
/// </summary>
public partial class NavigationBox : UserControl
{
    /// <summary>The tab this box belongs to, set once via <see cref="Initialize"/>.</summary>
    private ExplorerInstance? Instance { get; set; }

    internal void Initialize(ExplorerInstance instance)
    {
        Instance = instance;

        instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerInstance.EffectiveDevice))
                App.SafeInvoke(OnOwnerDeviceChanged);
        };
    }

    /// <summary>The breadcrumbs' labels and the drive restrictions were built for the previous
    /// device, and the path itself may be unchanged, so nothing else would rebuild them.</summary>
    private void OnOwnerDeviceChanged()
    {
        if (string.IsNullOrEmpty(Path))
            return;

        TrackPathRestrictions();
        Refresh();
    }

    private LogicalDeviceViewModel? _trackedDevice;

    public enum ViewMode
    {
        None,
        Breadcrumbs,
        Path,
    }

    public NavigationBox()
    {
        InitializeComponent();

        Breadcrumbs = [];

        Mode = ViewMode.None;

        SizeChanged += (sender, args) => ArrangeBreadcrumbs();

        // App-wide events reach every tab's box, but only the active tab's should react - and the
        // explorer's clear (e.g. device polling) must not blank the crumb of a page the tab is showing.
        Data.ClearNavigationBox += (s, e) =>
        {
            if (IsOwnedByActiveTab && Instance?.IsShowingPage is not true)
                Clear();
        };

        Data.UnfocusNavigationBox += (s, focus) =>
        {
            if (IsOwnedByActiveTab)
                Unfocus(focus);
        };
    }

    private bool IsOwnedByActiveTab => Instance is null || ReferenceEquals(Instance, Data.ActiveExplorerInstance);

    private void Clear()
    {
        App.SafeInvoke(() =>
        {
            _arrangeGeneration++;
            Path = null;
            DisplayPath = null;
            Items = [];
            breadcrumbs = [];
            itemWidths = [];
            locations = [];
            Mode = ViewMode.None;
            OverflowPopup.IsOpen = false;
        });
    }

    private void Unfocus(bool focus)
    {
        if (focus && Mode is not ViewMode.Path)
            Mode = ViewMode.Path;
        else
            FocusHelper.ClearFocus(this);
    }

    #region Dependency Properties

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set
        {
            bool update = Path != value;

            SetValue(PathProperty, value);

            // The saved-locations menu depends on the path, so don't wait for the deferred rebuild below.
            UpdateSavedItems();

            App.SafeBeginInvoke(() =>
            {
                OverflowPopup.IsOpen = false;

                if (update)
                {
                    if (string.IsNullOrEmpty(value))
                        UntrackDevice();
                    else
                        TrackPathRestrictions();

                    AddDevice(value);
                }
            }, DispatcherPriority.Render);
        }
    }

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register(nameof(Path), typeof(string),
          typeof(NavigationBox), new PropertyMetadata(null));

    public string? DisplayPath
    {
        get => (string?)GetValue(DisplayPathProperty);
        set => SetValue(DisplayPathProperty, value);
    }

    public static readonly DependencyProperty DisplayPathProperty =
        DependencyProperty.Register(nameof(DisplayPath), typeof(string),
          typeof(NavigationBox), new PropertyMetadata(null));

    public List<MenuItem> Breadcrumbs
    {
        get => (List<MenuItem>)GetValue(BreadcrumbsProperty);
        set => SetValue(BreadcrumbsProperty, value);
    }

    public static readonly DependencyProperty BreadcrumbsProperty =
        DependencyProperty.Register(nameof(Breadcrumbs), typeof(List<MenuItem>),
          typeof(NavigationBox), new PropertyMetadata(null));

    public bool HasDriveRestrictions
    {
        get => (bool)GetValue(HasDriveRestrictionsProperty);
        set => SetValue(HasDriveRestrictionsProperty, value);
    }

    public static readonly DependencyProperty HasDriveRestrictionsProperty =
        DependencyProperty.Register(nameof(HasDriveRestrictions), typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(false));

    public string RestrictionsTooltip
    {
        get => (string)GetValue(RestrictionsTooltipProperty);
        set => SetValue(RestrictionsTooltipProperty, value);
    }

    public static readonly DependencyProperty RestrictionsTooltipProperty =
        DependencyProperty.Register(nameof(RestrictionsTooltip), typeof(string),
          typeof(NavigationBox), new PropertyMetadata(""));

    public string RestrictionsIconGlyph
    {
        get => (string)GetValue(RestrictionsIconGlyphProperty);
        set => SetValue(RestrictionsIconGlyphProperty, value);
    }

    public static readonly DependencyProperty RestrictionsIconGlyphProperty =
        DependencyProperty.Register(nameof(RestrictionsIconGlyph), typeof(string),
          typeof(NavigationBox), new PropertyMetadata("\uE7BA"));

    public bool IsLoadingProgressVisible
    {
        get => (bool)GetValue(IsLoadingProgressVisibleProperty);
        set => SetValue(IsLoadingProgressVisibleProperty, value);
    }

    public static readonly DependencyProperty IsLoadingProgressVisibleProperty =
        DependencyProperty.Register(nameof(IsLoadingProgressVisible), typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(false));

    public Thickness MenuPadding
    {
        get => (Thickness)GetValue(MenuPaddingProperty);
        set => SetValue(MenuPaddingProperty, value);
    }

    public static readonly DependencyProperty MenuPaddingProperty =
        DependencyProperty.Register(nameof(MenuPadding), typeof(Thickness),
          typeof(NavigationBox), new PropertyMetadata(null));

    public ObservableList<IMenuItem> Items
    {
        get => (ObservableList<IMenuItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(ObservableList<IMenuItem>),
          typeof(NavigationBox), new PropertyMetadata(null));

    private readonly SavedLocation _sentinel = new();
    private readonly ObservableCollection<SavedLocation> _sentinelCollection = [];

    public CompositeCollection AllSavedItems { get; } = [];

    public ObservableList<SavedLocation> SavedItems
    {
        get => (ObservableList<SavedLocation>)GetValue(SavedItemsProperty);
        set => SetValue(SavedItemsProperty, value);
    }

    public static readonly DependencyProperty SavedItemsProperty =
        DependencyProperty.Register(nameof(SavedItems), typeof(ObservableList<SavedLocation>),
          typeof(NavigationBox), new PropertyMetadata(null, OnSavedItemsChanged));

    private static void OnSavedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NavigationBox)d;

        box.AllSavedItems.Clear();
        box.AllSavedItems.Add(new CollectionContainer { Collection = box._sentinelCollection });
        if (e.NewValue is ObservableList<SavedLocation> items)
        {
            box.AllSavedItems.Add(new CollectionContainer { Collection = items });
            items.CollectionChanged += (_, _) => box.UpdateSavedItems();
        }

        box.UpdateSavedItems();
    }

    public bool IsCurrentSaved
    {
        get => (bool)GetValue(IsCurrentSavedProperty);
        set => SetValue(IsCurrentSavedProperty, value);
    }

    public static readonly DependencyProperty IsCurrentSavedProperty =
        DependencyProperty.Register(nameof(IsCurrentSaved), typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(false));

    /// <summary>False when the saved-locations menu would be empty, e.g. in a device's drive
    /// view with nothing saved - there is no current location to add there.</summary>
    public bool HasSavedMenuItems
    {
        get => (bool)GetValue(HasSavedMenuItemsProperty);
        set => SetValue(HasSavedMenuItemsProperty, value);
    }

    public static readonly DependencyProperty HasSavedMenuItemsProperty =
        DependencyProperty.Register(nameof(HasSavedMenuItems), typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(true));

    #endregion

    public ViewMode Mode
    {
        get => (ViewMode)GetValue(ModeProperty);
        set
        {
            SetValue(ModeProperty, value);

            // Mirror onto this tab's own Instance so styles that used to bind to this control by
            // ElementName (now out of reach from ExplorerListHost) can react via the view model.
            if (Instance is not null)
                Instance.NavigationBoxMode = value;

            if (value is ViewMode.Path)
                PathBox.Focus();
            else if (PathBox.IsFocused)
                FocusHelper.ClearFocus(PathBox);
        }
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(ViewMode),
          typeof(NavigationBox), new PropertyMetadata(ViewMode.None));

    public double MenuHeight => Height - MenuPadding.Top - MenuPadding.Bottom;

    private void AddDevice(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // A page shows as a single crumb, not under the device's drive view.
        var driveView = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        if (path == driveView || new AdbLocation(AdbLocation.LocationFromString(path)).IsPage)
            PopulateButtons(path);
        else
            PopulateButtons(driveView + path);

        UpdateSavedItems();
    }

    private LogicalDeviceViewModel? OwnerDevice => Instance?.EffectiveDevice;

    private void UpdateSavedItems()
    {
        IsCurrentSaved = SavedItems?.Any(i => i.Path == Path) is true;

        var sentinelVisible = AdbLocation.LocationFromString(Path) is Navigation.SpecialLocation.None && !IsCurrentSaved;
        if (sentinelVisible && _sentinelCollection.Count == 0)
            _sentinelCollection.Add(_sentinel);
        else if (!sentinelVisible && _sentinelCollection.Count > 0)
            _sentinelCollection.Clear();

        HasSavedMenuItems = sentinelVisible || SavedItems?.Count > 0;
    }

    public void Refresh() => AddDevice(Path);

    public static IEnumerable<AdbLocation> SeparatePath(string path, LogicalDeviceViewModel? device = null)
    {
        string current = path;

        var driveView = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        if (path.StartsWith(driveView))
        {
            yield return new(Navigation.SpecialLocation.DriveView);
            current = current[driveView.Length..];
        }

        if (current.Length == 0)
            yield break;

        var searchModePath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.SearchMode);
        if (current.EndsWith(searchModePath, StringComparison.Ordinal))
        {
            var pathBeforeSearch = current.Length == searchModePath.Length
                ? ""
                : current[..^searchModePath.Length];

            if (!string.IsNullOrEmpty(pathBeforeSearch))
            {
                foreach (var loc in SeparatePath($"{driveView}{pathBeforeSearch}", device))
                {
                    if (loc.Location is Navigation.SpecialLocation.DriveView)
                        continue;

                    yield return loc;
                }
            }

            yield return new(Navigation.SpecialLocation.SearchMode);
            yield break;
        }

        if (AdbLocation.LocationFromString(current) is Navigation.SpecialLocation special
            and not Navigation.SpecialLocation.None)
        {
            yield return new(special);
            yield break;
        }

        var deviceId = device?.ID;
        var pairs = Data.CurrentDisplayNames.Where(kv => kv.Key.DeviceId == deviceId && current.StartsWith(kv.Key.Path));
        var drive = pairs.Count() > 1
            ? pairs.OrderBy(kv => kv.Key.Path.Length).Last()
            : pairs.FirstOrDefault();

        if (string.IsNullOrEmpty(drive.Key.Path))
            yield break;

        yield return new(drive.Key.Path);

        if (current.Length == 0)
            yield break;

        var index = drive.Key.Path.Length;

        if (current.Length == index)
            yield break;

        var tail = current[index..].TrimStart('/');
        if (string.IsNullOrEmpty(tail))
            yield break;

        var fullPath = FileHelper.ConcatPaths(drive.Key.Path, tail);
        var prefix = drive.Key.Path;

        if (ArchivePath.TryParse(fullPath, out var archivePath, out var internalPath, deviceId))
        {
            var afterDrive = archivePath[drive.Key.Path.Length..].TrimStart('/');
            var deviceSegments = afterDrive.Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in deviceSegments[..Math.Max(0, deviceSegments.Length - 1)])
            {
                prefix = $"{prefix}/{segment}";
                yield return new(prefix);
            }

            yield return new(ArchivePath.Join(archivePath, ""));

            if (!string.IsNullOrEmpty(internalPath))
            {
                var internalPrefix = "";
                foreach (var segment in internalPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    internalPrefix = string.IsNullOrEmpty(internalPrefix)
                        ? segment
                        : $"{internalPrefix}/{segment}";
                    yield return new(ArchivePath.Join(archivePath, internalPrefix));
                }
            }

            yield break;
        }

        foreach (var segment in tail.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            prefix = $"{prefix}/{segment}";
            yield return new(prefix);
        }
    }

    List<AdbLocation> locations = [];
    List<TextMenu> breadcrumbs = [];
    List<double> itemWidths = [];
    double excessButtonWidth;

    private void PopulateButtons(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var device = OwnerDevice;
        locations = SeparatePath(path, device).ToList();
        breadcrumbs = [.. locations.Select(item => item.GetNameSubMenu(device))];

        if (breadcrumbs.Count == 0)
            return;

        if (device?.Root is RootStatus.Enabled)
            breadcrumbs[0].Appearance = Wpf.Ui.Controls.ControlAppearance.Caution;

        breadcrumbs[^1].IsLast = true;

        itemWidths = [];
        _arrangeGeneration++;
        Items = [.. breadcrumbs];
        QueueMeasureAndArrange();
    }

    private int _arrangeGeneration;

    private void ArrangeBreadcrumbs()
    {
        if (breadcrumbs.Count == 0)
            return;

        if (itemWidths.Count == breadcrumbs.Count && itemWidths.TrueForAll(static w => w > 0))
        {
            ApplyCollapseArrangement();
            return;
        }

        // Widths are unknown: show every crumb, then measure real containers after layout.
        if (Items is null || Items.Count != breadcrumbs.Count)
            Items = [.. breadcrumbs];

        QueueMeasureAndArrange();
    }

    private void QueueMeasureAndArrange()
    {
        var generation = _arrangeGeneration;
        App.SafeBeginInvoke(() => CompleteMeasureAndArrange(generation), DispatcherPriority.Loaded);
    }

    private void CompleteMeasureAndArrange(int generation, bool isRetry = false)
    {
        if (generation != _arrangeGeneration)
            return;

        PathItemsControl.UpdateLayout();

        if (!TryCaptureRenderedItemWidths())
        {
            if (!isRetry)
                App.SafeBeginInvoke(() => CompleteMeasureAndArrange(generation, isRetry: true), DispatcherPriority.ContextIdle);
            return;
        }

        if (excessButtonWidth <= 0)
            TryCaptureExcessButtonWidth();

        ApplyCollapseArrangement();
    }

    private void TryCaptureExcessButtonWidth()
    {
        var excess = new TextMenu(new FileAction(FileAction.FileActionType.None, () => true, () => { }, "\uE712"))
        {
            Children = [],
        };

        Items = [.. breadcrumbs, excess];
        PathItemsControl.UpdateLayout();

        if (PathItemsControl.ItemContainerGenerator.ContainerFromIndex(breadcrumbs.Count) is FrameworkElement container)
            excessButtonWidth = ControlSize.GetWidth(container);

        Items = [.. breadcrumbs];
    }

    private bool TryCaptureRenderedItemWidths()
    {
        if (breadcrumbs.Count == 0 || PathItemsControl.Items.Count != breadcrumbs.Count)
            return false;

        var widths = new List<double>(breadcrumbs.Count);
        for (var i = 0; i < breadcrumbs.Count; i++)
        {
            if (PathItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                return false;

            var width = ControlSize.GetWidth(container);
            if (width <= 0)
                return false;

            widths.Add(width);
        }

        itemWidths = widths;
        return true;
    }

    private void ApplyCollapseArrangement()
    {
        int lastHiddenIndex = -1;
        for (var i = 1; i < breadcrumbs.Count; i++)
        {
            if (excessButtonWidth + itemWidths[0] + itemWidths[i..].Sum() > PathItemsControl.ActualWidth)
            {
                lastHiddenIndex = i;
            }
        }

        if (lastHiddenIndex == -1)
            Items = [.. breadcrumbs];
        else
        {
            var excessButton = new TextMenu(
                new FileAction(FileAction.FileActionType.None, () => true, () => { }, "\uE712"))
            {
                Children = locations[1..(lastHiddenIndex + 1)].Select(item => item.GetExcessSubMenu(OwnerDevice))
            };

            var itemsControl = OverflowItemsControl;
            itemsControl.ItemsSource = excessButton.Children;
            var remainingCrumbs = breadcrumbs[(lastHiddenIndex + 1)..];

            excessButton.IsLast = remainingCrumbs.Count == 0;

            Items = [breadcrumbs[0], excessButton, .. remainingCrumbs];
        }
    }

    private void PathBox_GotFocus(object sender, RoutedEventArgs e)
    {
        Mode = ViewMode.Path;

        DisplayPath = AdbLocation.LocationFromString(Path) is Navigation.SpecialLocation.None ? Path : "";

        App.SafeBeginInvoke(PathBox.SelectAll);
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Enter && PathBox.Text == ""))
        {
            e.Handled = true;
            Mode = ViewMode.Breadcrumbs;
        }
        else if (e.Key == Key.Enter)
        {
            Data.RuntimeSettings.PathBoxNavigation = AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(p => DisplayPath?.StartsWith(p) == true)
                ? AdbExplorerConst.RECYCLE_PATH
                : DisplayPath ?? "";

            e.Handled = true;
            Mode = ViewMode.Breadcrumbs;
        }
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Mode = ViewMode.Breadcrumbs;
    }

    private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TextMenu { Children: not null })
        {
            OverflowPopup.PlacementTarget = fe;
            OverflowPopup.IsOpen = true;
        }
    }

    private void TrackPathRestrictions()
    {
        var device = OwnerDevice;
        if (_trackedDevice != device)
        {
            UntrackDevice();
            _trackedDevice = device;
            if (_trackedDevice is not null)
                _trackedDevice.PropertyChanged += OnTrackedDevicePropertyChanged;
        }

        ApplyDriveRestrictions();

        if (AdbHelper.NeedsMountInfo(_trackedDevice))
            _ = Task.Run(() => AdbHelper.ApplyMountInfo(_trackedDevice, Data.DeviceCts.Token), Data.DeviceCts.Token);
    }

    private void UntrackDevice()
    {
        if (_trackedDevice is null)
            return;

        _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
        _trackedDevice = null;
    }

    private void OnTrackedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogicalDeviceViewModel.Mounts)
            or nameof(LogicalDeviceViewModel.HasRootShell))
            App.SafeInvoke(ApplyDriveRestrictions);
        else if (e.PropertyName is nameof(LogicalDeviceViewModel.Name))
            App.SafeInvoke(Refresh);
    }

    private void ApplyDriveRestrictions()
    {
        var path = Path;
        var device = _trackedDevice ?? OwnerDevice;
        var deviceId = device?.ID;
        var isArchive = ArchivePath.IsArchivePath(path ?? "", deviceId);
        var restrictions = DriveHelper.GetRestrictions(path ?? "", device);

        string tooltipText;
        string iconGlyph;

        if (isArchive)
        {
            var archiveDevicePath = ArchivePath.GetArchivePath(path ?? "", deviceId);
            tooltipText = ArchiveHelper.GetArchiveModificationTooltip(
                FileHelper.GetFullName(archiveDevicePath),
                deviceId ?? "");

            iconGlyph = "\uF012";
            HasDriveRestrictions = true;
        }
        else
        {
            tooltipText = restrictions.GetTooltipText();
            iconGlyph = restrictions.IconGlyph;
            HasDriveRestrictions = restrictions.HasAny;
        }

        RestrictionsTooltip = tooltipText;
        RestrictionsIconGlyph = string.IsNullOrEmpty(iconGlyph) ? "\uE7BA" : iconGlyph;

        RestrictionsToolTip.Content = string.IsNullOrEmpty(tooltipText)
            ? null
            : new TextBlock
            {
                Text = tooltipText,
                TextWrapping = TextWrapping.Wrap,
            };
    }

    private void RestrictionsIcon_Click(object sender, RoutedEventArgs e)
    {
        ApplyDriveRestrictions();
        RestrictionsToolTip.IsOpen = true;
    }

    private void RestrictionsIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        RestrictionsToolTip.IsOpen = false;
    }
}
