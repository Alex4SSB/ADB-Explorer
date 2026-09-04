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

        Data.ClearNavigationBox += (s, e) => Clear();

        Data.UnfocusNavigationBox += (s, focus) => Unfocus(focus);
    }

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
            UnfocusTarget?.Focus();
    }

    #region Dependency Properties

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set
        {
            bool update = Path != value;

            SetValue(PathProperty, value);

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

    public UIElement UnfocusTarget
    {
        get => (UIElement)GetValue(UnfocusTargetProperty);
        set => SetValue(UnfocusTargetProperty, value);
    }

    public static readonly DependencyProperty UnfocusTargetProperty =
        DependencyProperty.Register(nameof(UnfocusTarget), typeof(UIElement),
          typeof(NavigationBox), new PropertyMetadata(null));

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

    #endregion

    public ViewMode Mode
    {
        get => (ViewMode)GetValue(ModeProperty);
        set
        {
            SetValue(ModeProperty, value);

            if (value is ViewMode.Path)
                PathBox.Focus();
            else if (UnfocusTarget is not null && PathBox.IsFocused)
                UnfocusTarget?.Focus();
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

        var driveView = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        if (path == driveView)
            PopulateButtons(path);
        else
            PopulateButtons(driveView + path);

        UpdateSavedItems();
    }

    private void UpdateSavedItems()
    {
        IsCurrentSaved = SavedItems?.Any(i => i.Path == Path) is true;

        var sentinelVisible = AdbLocation.LocationFromString(Path) is Navigation.SpecialLocation.None && !IsCurrentSaved;
        if (sentinelVisible && _sentinelCollection.Count == 0)
            _sentinelCollection.Add(_sentinel);
        else if (!sentinelVisible && _sentinelCollection.Count > 0)
            _sentinelCollection.Clear();
    }

    public void Refresh() => AddDevice(Path);

    public static IEnumerable<AdbLocation> SeparatePath(string path)
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
                foreach (var loc in SeparatePath($"{driveView}{pathBeforeSearch}"))
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

        var pairs = Data.CurrentDisplayNames.Where(kv => current.StartsWith(kv.Key));
        var drive = pairs.Count() > 1
            ? pairs.OrderBy(kv => kv.Key.Length).Last()
            : pairs.FirstOrDefault();

        if (string.IsNullOrEmpty(drive.Key))
            yield break;

        yield return new(drive.Key);

        if (current.Length == 0)
            yield break;

        var index = drive.Key.Length;

        if (current.Length == index)
            yield break;

        var tail = current[index..].TrimStart('/');
        if (string.IsNullOrEmpty(tail))
            yield break;

        var deviceId = Data.DevicesObject?.Current?.ID;
        var fullPath = FileHelper.ConcatPaths(drive.Key, tail);
        var prefix = drive.Key;

        if (ArchivePath.TryParse(fullPath, out var archivePath, out var internalPath, deviceId))
        {
            var afterDrive = archivePath[drive.Key.Length..].TrimStart('/');
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

        locations = SeparatePath(path).ToList();
        breadcrumbs = [.. locations.Select(item => item.NameSubMenu)];

        if (breadcrumbs.Count == 0)
            return;

        if (Data.DevicesObject?.Current?.Root is RootStatus.Enabled)
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
                Children = locations[1..(lastHiddenIndex + 1)].Select(item => item.ExcessSubMenu)
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
        var device = Data.DevicesObject?.Current;
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
    }

    private void ApplyDriveRestrictions()
    {
        var path = Path;
        var device = _trackedDevice ?? Data.DevicesObject?.Current;
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
