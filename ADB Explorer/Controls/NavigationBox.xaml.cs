using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

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

        Breadcrumbs = new();

        Mode = ViewMode.None;
    }

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set
        {
            if (Path != value)
            {
                PopulateButtons(value);
            }

            SetValue(PathProperty, value);
        }
    }

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

    public string DriveIcon
    {
        get => (string)GetValue(DriveIconProperty);
        set => SetValue(DriveIconProperty, value);
    }

    public static readonly DependencyProperty DriveIconProperty =
        DependencyProperty.Register("DriveIcon", typeof(string),
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

    public void Refresh() => PopulateButtons(Path);

    private void PopulateButtons(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var expectedLength = 0.0;
        List<MenuItem> tempButtons = new();
        List<string> pathItems = new();

        var pairs = DisplayNames.Where(kv => path.StartsWith(kv.Key));
        var specialPair = pairs.Count() > 1 ? pairs.OrderBy(kv => kv.Key.Length).Last() : pairs.First();
        if (specialPair.Key != null)
        {
            MenuItem button = CreatePathButton(specialPair);
            tempButtons.Add(button);
            pathItems.Add(specialPair.Key);
            path = path[specialPair.Key.Length..].TrimStart('/');
            expectedLength = ControlSize.GetWidth(button);
        }

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

        int i = 0;
        for (; i < Breadcrumbs.Count && i < tempButtons.Count; i++)
        {
            var oldB = Breadcrumbs[i];
            var newB = tempButtons[i];
            if (oldB.Header.ToString() != newB.Header.ToString() ||
                TextHelper.GetAltObject(oldB).ToString() != TextHelper.GetAltObject(newB).ToString())
            {
                break;
            }
        }
        Breadcrumbs.RemoveRange(i, Breadcrumbs.Count - i);
        Breadcrumbs.AddRange(tempButtons.GetRange(i, tempButtons.Count - i));

        ConsolidateButtons(expectedLength);
    }

    private void ConsolidateButtons(double expectedLength)
    {
        if (expectedLength > PathBox.ActualWidth)
            expectedLength += ControlSize.GetWidth(CreateExcessButton());

        double excessLength = expectedLength - PathBox.ActualWidth;
        List<MenuItem> excessButtons = new();
        BreadcrumbMenu.Items.Clear();

        if (excessLength > 0)
        {
            int i = 0;
            while (excessLength >= 0 && Breadcrumbs.Count - excessButtons.Count > 1)
            {
                excessButtons.Add(Breadcrumbs[i]);
                Breadcrumbs[i].ContextMenu = null;
                Breadcrumbs[i].Height = double.NaN;
                Breadcrumbs[i].Padding = new(10, 4, 10, 4);
                Breadcrumbs[i].Icon = new FontIcon()
                {
                    Glyph = string.IsNullOrEmpty(DriveIcon) ? "\uE8B7" : DriveIcon,
                };

                Breadcrumbs[i].Margin = Data.Settings.UseFluentStyles ? new(5, 1, 5, 1) : new(0);
                ControlHelper.SetCornerRadius(Breadcrumbs[i], new(Data.Settings.UseFluentStyles ? 4 : 0));

                excessLength -= ControlSize.GetWidth(Breadcrumbs[i]);

                i++;
            }

            AddExcessButton(excessButtons);
        }

        foreach (var item in Breadcrumbs.Except(excessButtons))
        {
            if (BreadcrumbMenu.Items.Count > 0)
                AddPathArrow();

            AddPathButton(item);
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

    private MenuItem CreateExcessButton()
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
        if (excessButtons is not null && !excessButtons.Any())
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

    private void AddPathButton(MenuItem button)
    {
        if (TextHelper.GetAltObject(button) is string str && str == AdbExplorerConst.RECYCLE_PATH)
            button.ContextMenu = null;

        button.Height = 24;
        button.Padding = new(8, 0, 8, 0);
        button.Margin = new(0);

        ControlHelper.SetCornerRadius(button, new(Data.Settings.UseFluentStyles ? 3 : 0));

        BreadcrumbMenu.Items.Add(button);
    }

    private MenuItem CreatePathArrow() => new()
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

    private void AddPathArrow(bool append = true)
    {
        var arrow = CreatePathArrow();

        if (append)
            BreadcrumbMenu.Items.Add(arrow);
        else
            BreadcrumbMenu.Items.Insert(0, arrow);
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
            Data.RuntimeSettings.PathBoxNavigation = DisplayPath.StartsWith(AdbExplorerConst.RECYCLE_PATH)
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
