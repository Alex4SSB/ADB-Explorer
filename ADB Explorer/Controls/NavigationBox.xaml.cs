using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NavigationBox.xaml
/// </summary>
public partial class NavigationBox : UserControl
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
    }

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set
        {
            if (Path != value)
            {
                AddDevice(value);
            }

            SetValue(PathProperty, value);
        }
    }

    private bool isFuse = false;

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register("Path", typeof(string),
          typeof(NavigationBox), new PropertyMetadata(null));

    public string DisplayPath
    {
        get => (string)GetValue(DisplayPathProperty);
        set => SetValue(DisplayPathProperty, value);
    }

    public static readonly DependencyProperty DisplayPathProperty =
        DependencyProperty.Register("DisplayPath", typeof(string),
          typeof(NavigationBox), new PropertyMetadata(null));

    public Dictionary<string, string> DisplayNames
    {
        get => (Dictionary<string, string>)GetValue(DisplayNamesProperty);
        set => SetValue(DisplayNamesProperty, value);
    }

    public static readonly DependencyProperty DisplayNamesProperty =
        DependencyProperty.Register("DisplayNames", typeof(Dictionary<string, string>),
          typeof(NavigationBox), new PropertyMetadata(null));

    public List<MenuItem> Breadcrumbs
    {
        get => (List<MenuItem>)GetValue(BreadcrumbsProperty);
        set => SetValue(BreadcrumbsProperty, value);
    }

    public static readonly DependencyProperty BreadcrumbsProperty =
        DependencyProperty.Register("Breadcrumbs", typeof(List<MenuItem>),
          typeof(NavigationBox), new PropertyMetadata(null));

    public bool IsLoadingProgressVisible
    {
        get => (bool)GetValue(IsLoadingProgressVisibleProperty);
        set => SetValue(IsLoadingProgressVisibleProperty, value);
    }

    public static readonly DependencyProperty IsLoadingProgressVisibleProperty =
        DependencyProperty.Register("IsLoadingProgressVisible", typeof(bool),
          typeof(NavigationBox), new PropertyMetadata(false));

    public UIElement UnfocusTarget
    {
        get => (UIElement)GetValue(UnfocusTargetProperty);
        set => SetValue(UnfocusTargetProperty, value);
    }

    public static readonly DependencyProperty UnfocusTargetProperty =
        DependencyProperty.Register("UnfocusTarget", typeof(UIElement),
          typeof(NavigationBox), new PropertyMetadata(null));

    public Thickness MenuPadding
    {
        get => (Thickness)GetValue(MenuPaddingProperty);
        set => SetValue(MenuPaddingProperty, value);
    }

    public static readonly DependencyProperty MenuPaddingProperty =
        DependencyProperty.Register("MenuPadding", typeof(Thickness),
          typeof(NavigationBox), new PropertyMetadata(null));

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
        DependencyProperty.Register("Mode", typeof(ViewMode),
          typeof(NavigationBox), new PropertyMetadata(ViewMode.None));

    public double MenuHeight => Height - MenuPadding.Top - MenuPadding.Bottom;

    public Thickness MenuMargin => new(MenuPadding.Left, 0, 0, 0);

    private void AddDevice(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var driveView = NavHistory.StringFromLocation(NavHistory.SpecialLocation.DriveView);
        if (path == driveView)
            PopulateButtons(path);
        else
            PopulateButtons(driveView + path);

        if (string.IsNullOrWhiteSpace(driveView) && Data.DevicesObject.Current is LogicalDeviceViewModel device)
        {
            Data.DevicesObject.Current.PropertyChanged += Device_PropertyChanged;
        }
    }

    private void Device_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogicalDevice.Name))
        {
            FolderHelper.CombineDisplayNames();
            Refresh();

            if (!string.IsNullOrWhiteSpace(Data.DevicesObject?.Current?.Name))
                Data.DevicesObject.Current.PropertyChanged -= Device_PropertyChanged;
        }
    }

    public void Refresh() => AddDevice(Path);

    private void PopulateButtons(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var expectedLength = 0.0;
        List<MenuItem> tempButtons = [];
        List<string> pathItems = [];

        if (path.StartsWith(NavHistory.StringFromLocation(NavHistory.SpecialLocation.DriveView)))
        {
            AddSpecialButton(ref path,
                             ref expectedLength,
                             ref tempButtons,
                             DisplayNames.FirstOrDefault(kv => path.StartsWith(kv.Key)),
                             false);
        }
        
        isFuse = DriveHelper.GetCurrentDrive(path)?.IsFUSE is true;

        var pairs = DisplayNames.Where(kv => path.StartsWith(kv.Key));
        var specialPair = pairs.Count() > 1 ? pairs.OrderBy(kv => kv.Key.Length).Last() : pairs.FirstOrDefault();
        
        AddSpecialButton(ref path, ref expectedLength, ref tempButtons, specialPair);
        pathItems.Add(specialPair.Key);

        var dirs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            pathItems.Add(dir);
            var dirPath = string.Join('/', pathItems).Replace("//", "/");
            MenuItem button = CreatePathButton(dirPath, dir);
            tempButtons.Add(button);
            expectedLength += ControlSize.GetWidth(button);
        }

        expectedLength += (tempButtons.Count - 1) * ControlSize.GetWidth(CreatePathArrow());

        // Refresh optimization - do not overwrite breadcrumbs unnecessarily
        int i = 0;
        for (; i < Breadcrumbs.Count && i < tempButtons.Count; i++)
        {
            var oldB = Breadcrumbs[i];
            var newB = tempButtons[i];
            if (((TextBlock)oldB.Header).Text != ((TextBlock)newB.Header).Text ||
                TextHelper.GetAltObject(oldB).ToString() != TextHelper.GetAltObject(newB).ToString())
            {
                break;
            }
        }
        Breadcrumbs.RemoveRange(i, Breadcrumbs.Count - i);
        Breadcrumbs.AddRange(tempButtons.GetRange(i, tempButtons.Count - i));

        ConsolidateButtons(expectedLength);

        void AddSpecialButton(ref string path,
                              ref double expectedLength,
                              ref List<MenuItem> tempButtons,
                              KeyValuePair<string, string> specialPair,
                              bool trimStart = true)
        {
            if (specialPair.Key is null)
                return;

            MenuItem button = CreatePathButton(specialPair);
            tempButtons.Add(button);

            path = path[specialPair.Key.Length..];
            if (trimStart)
                path = path.TrimStart();

            expectedLength += ControlSize.GetWidth(button);
        }
    }

    private void ConsolidateButtons(double expectedLength)
    {
        if (isFuse)
        {
            expectedLength += ControlSize.GetWidth(CreateFuseIcon());
        }

        if (expectedLength > PathBox.ActualWidth)
            expectedLength += ControlSize.GetWidth(CreateExcessButton());

        double excessLength = expectedLength - PathBox.ActualWidth;
        List<MenuItem> excessButtons = [];
        BreadcrumbMenu.Items.Clear();

        if (excessLength > 0)
        {
            int i = 1;
            while (excessLength >= 0 && Breadcrumbs.Count - excessButtons.Count > 0)
            {
                var path = TextHelper.GetAltObject(Breadcrumbs[i]).ToString();
                var drives = Data.DevicesObject.Current.Drives.Where(drive => drive.Path == path);
                var icon = "\uE8B7";
                if (drives.Any())
                    icon = drives.First().DriveIcon;

                excessButtons.Add(Breadcrumbs[i]);
                Breadcrumbs[i].ContextMenu = null;
                Breadcrumbs[i].Height = double.NaN;
                Breadcrumbs[i].Padding = new(10, 4, 10, 4);
                Breadcrumbs[i].Icon = new FontIcon() { Glyph = icon };

                Breadcrumbs[i].Margin = Data.RuntimeSettings.UseFluentStyles ? new(5, 1, 5, 1) : new(0);
                ControlHelper.SetCornerRadius(Breadcrumbs[i], new(Data.RuntimeSettings.UseFluentStyles ? 4 : 0));

                excessLength -= ControlSize.GetWidth(Breadcrumbs[i]);

                i++;
            }

            AddExcessButton(excessButtons);
        }

        foreach (var item in Breadcrumbs.Except(excessButtons))
        {
            if (BreadcrumbMenu.Items.Count == 1 && ((MenuItem)BreadcrumbMenu.Items[0]).HasItems)
            {
                AddPathButton(item, 0);
                AddPathArrow(1);
                continue;
            }

            if (BreadcrumbMenu.Items.Count > 0)
                AddPathArrow();

            AddPathButton(item);
        }

        if (isFuse)
        {
            AddFuseIcon();
        }

        if (excessLength > 0)
        {
            var width = Breadcrumbs[^1].ActualWidth - (ControlSize.GetWidth(BreadcrumbMenu) - PathBox.ActualWidth) - 4;
            if (width < 0)
                width = 0;

            Breadcrumbs[^1].Width = width;
        }
        else
            Breadcrumbs[^1].Width = double.NaN;
    }

    private static MenuItem CreateExcessButton()
    {
        var menuItem = new MenuItem()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Height = 24,
            Padding = new(10, 4, 10, 4),
            Margin = new(0),
            Header = new FontIcon()
            {
                Glyph = "\uE712",
                FontSize = 18,
            },
        };

        return menuItem;
    }

    private void AddExcessButton(List<MenuItem> excessButtons = null)
    {
        if (excessButtons is not null && excessButtons.Count == 0)
            return;

        var button = CreateExcessButton();
        button.ItemsSource = excessButtons;

        BreadcrumbMenu.Items.Add(button);
    }

    private MenuItem CreatePathButton(KeyValuePair<string, string> kv) => CreatePathButton(kv.Key, kv.Value);
    private MenuItem CreatePathButton(object path, string name)
    {
        MenuItem button = new()
        {
            Header = new TextBlock() { Text = name, Margin = new(0, 0, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0),
            Padding = new(8, 0, 8, 0),
            Height = 24,
        };
        button.Click += PathButton_Click;
        TextHelper.SetAltObject(button, path);

        return button;
    }

    private void AddPathButton(MenuItem button, int index = -1)
    {
        if (TextHelper.GetAltObject(button) is string str && str == AdbExplorerConst.RECYCLE_PATH)
            button.ContextMenu = null;

        button.Height = 24;
        button.Padding = new(8, 0, 8, 0);
        button.Margin = new(0);

        ControlHelper.SetCornerRadius(button, new(Data.RuntimeSettings.UseFluentStyles ? 3 : 0));

        if (index < 0)
            BreadcrumbMenu.Items.Add(button);
        else
            BreadcrumbMenu.Items.Insert(index, button);
    }

    private static MenuItem CreatePathArrow() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Height = 24,
        Margin = new(0),
        Padding = new(3, 0, 3, 0),
        IsEnabled = false,
        Header = new FontIcon()
        {
            Glyph = "\uE970",
            FontSize = 8,
        },
    };

    private void AddPathArrow(int index = -1)
    {
        var arrow = CreatePathArrow();

        if (index < 0)
            BreadcrumbMenu.Items.Add(arrow);
        else
            BreadcrumbMenu.Items.Insert(index, arrow);
    }

    private static MenuItem CreateFuseIcon()
    {
        MenuItem menuItem = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Height = 24,
            Margin = new(0, 0, 4, 0),
            Padding = new(3, 0, 3, 0),
            IsChecked = true,
            Header = new FontIcon()
            {
                Glyph = "\uF0EF",
                FontSize = 20,
            },
            ToolTip = Strings.S_FUSE_DRIVE_TOOLTIP,
        };

        MenuHelper.SetIsMouseSelectionVisible(menuItem, false);

        return menuItem;
    }

    private void AddFuseIcon()
    {
        BreadcrumbMenu.Items.Insert(0, CreateFuseIcon());
    }

    private void PathButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            Data.RuntimeSettings.LocationToNavigate = TextHelper.GetAltObject(item);
        }
    }

    private void PathBox_GotFocus(object sender, RoutedEventArgs e)
    {
        Mode = ViewMode.Path;

        DisplayPath = NavHistory.LocationFromString(Path) is NavHistory.SpecialLocation.None ? Path : "";

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
            Data.RuntimeSettings.PathBoxNavigation = AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(path => DisplayPath.StartsWith(path))
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
}
