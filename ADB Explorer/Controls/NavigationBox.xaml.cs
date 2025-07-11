using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NavigationBox.xaml
/// </summary>
public partial class NavigationBox
{
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

        Data.RuntimeSettings.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(AppRuntimeSettings.LocationToNavigate))
            {
                FlyoutService.GetFlyout(SavedItemsButton).Hide();
            }
            else if (args.PropertyName == nameof(AppRuntimeSettings.SavedLocations))
            {
                UpdateSavedItems();
            }
        };
    }

    #region Dependency Properties

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set
        {
            bool update = Path != value;

            SetValue(PathProperty, value);
            
            if (update)
            {
                IsFUSE = DriveHelper.GetCurrentDrive(value)?.IsFUSE is true;
                AddDevice(value);
            }
        }
    }

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register(nameof(Path), typeof(string),
          typeof(NavigationBox), new PropertyMetadata(null));

    public string DisplayPath
    {
        get => (string)GetValue(DisplayPathProperty);
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

    public bool IsFUSE
    {
        get => (bool)GetValue(IsFUSEProperty);
        set => SetValue(IsFUSEProperty, value);
    }

    public static readonly DependencyProperty IsFUSEProperty =
        DependencyProperty.Register(nameof(IsFUSE), typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(false));

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

    public ObservableList<SavedLocation> SavedItems
    {
        get => (ObservableList<SavedLocation>)GetValue(SavedItemsProperty);
        set => SetValue(SavedItemsProperty, value);
    }

    public static readonly DependencyProperty SavedItemsProperty =
        DependencyProperty.Register(nameof(SavedItems), typeof(ObservableList<SavedLocation>),
          typeof(NavigationBox), new PropertyMetadata(null));

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
                UnfocusTarget.Focus();
        }
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(ViewMode),
          typeof(NavigationBox), new PropertyMetadata(ViewMode.None));

    public double MenuHeight => Height - MenuPadding.Top - MenuPadding.Bottom;

    private void AddDevice(string path)
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
        SavedItems = Data.RuntimeSettings.SavedLocations is null
            ? []
            : [.. Data.RuntimeSettings.SavedLocations.Select(p => new SavedLocation(p))];

        IsCurrentSaved = SavedItems.Any(i => i.Path == Path);

        if (AdbLocation.LocationFromString(Path) is Navigation.SpecialLocation.None && !IsCurrentSaved)
            SavedItems.Insert(0, new SavedLocation());

        ((ItemsControl)Resources["SavedItemsControl"]).ItemsSource = SavedItems;
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

        var pairs = Data.CurrentDisplayNames.Where(kv => current.StartsWith(kv.Key));
        var drive = pairs.Count() > 1
            ? pairs.OrderBy(kv => kv.Key.Length).Last()
            : pairs.FirstOrDefault();

        yield return new(drive.Key);

        if (current.Length == 0)
            yield break;

        var index = drive.Key.Length;

        if (current.Length == index)
            yield break;

        while (index >= 0)
        {
            if (current.Length <= index)
                break;

            var next = current.IndexOf('/', index + 1);

            yield return new(current[..(next < 0 ? ^0 : next)]);

            index = next;
        }
    }

    IEnumerable<AdbLocation> locations = [];
    List<TextMenu> breadcrumbs = [];
    List<double> itemWidths = [];

    private void PopulateButtons(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        locations = SeparatePath(path);
        breadcrumbs = locations.Select(item => item.NameSubMenu).ToList();
        breadcrumbs[^1].IsLast = true;

        var template = (DataTemplate)Resources["BreadcrumbTemplate"];
        itemWidths = [.. breadcrumbs.Select(item => GetTextWidth(item) + ControlSize.GetWidth(template, item))];

        ArrangeBreadcrumbs();
    }

    private void ArrangeBreadcrumbs()
    {
        if (breadcrumbs.Count == 0)
            return;

        int lastHiddenIndex = -1;
        for (var i = 1; i < breadcrumbs.Count; i++)
        {
            if (125 + itemWidths[0] + itemWidths[i..].Sum() > PathBox.ActualWidth)
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
                Children = locations.ToList()[1..(lastHiddenIndex + 1)].Select(item => item.ExcessSubMenu)
            };

            var itemsControl = (ItemsControl)Resources["OverflowItemsControl"];
            itemsControl.ItemsSource = excessButton.Children;

            Items = [breadcrumbs[0], excessButton, .. breadcrumbs[(lastHiddenIndex + 1)..]];
        }
    }

    private static double GetTextWidth(TextMenu textMenu)
    {
        TextBlock textBlock = new() { Text = textMenu.Action.Description };
        return ControlSize.GetWidth(textBlock);
    }

    private void PathBox_GotFocus(object sender, RoutedEventArgs e)
    {
        Mode = ViewMode.Path;

        DisplayPath = AdbLocation.LocationFromString(Path) is Navigation.SpecialLocation.None ? Path : "";

        PathBox.SelectAll();
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
            Data.RuntimeSettings.PathBoxNavigation = AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(DisplayPath.StartsWith)
                ? AdbExplorerConst.RECYCLE_PATH
                : DisplayPath;

            e.Handled = true;
            Mode = ViewMode.Breadcrumbs;
        }
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Mode = ViewMode.Breadcrumbs;
    }

    private void FuseIcon_Click(object sender, RoutedEventArgs e)
    {
        ((ToolTip)FuseIcon.ToolTip).IsOpen = true;
    }

    private void FuseIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        ((ToolTip)FuseIcon.ToolTip).IsOpen = false;
    }
}
