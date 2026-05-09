using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ThumbsSizeSelector.xaml
/// </summary>
[ObservableObject]
public partial class ThumbsSizeSelector : UserControl
{
    public ThumbsSizeSelector()
    {
        Items = [
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_DETAILS, ThumbnailService.ThumbnailSize.Disabled, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_MEDIUM, ThumbnailService.ThumbnailSize.Medium, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_LARGE, ThumbnailService.ThumbnailSize.Large, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_XL, ThumbnailService.ThumbnailSize.ExtraLarge, this),
            new Separator(),
            new SidePaneModeItem(Strings.Resources.S_THUMBSIZE_DETAILS, AppSettings.SidePaneMode.Details),
            new SidePaneModeItem(Strings.Resources.S_SIDE_PANE_PREVIEW, AppSettings.SidePaneMode.Preview),
        ];

        InitializeComponent();
    }

    static Dictionary<ThumbnailService.ThumbnailSize, UIElement> Icons => new()
    {
        { ThumbnailService.ThumbnailSize.Disabled, new FontIcon() { Glyph = Data.RuntimeSettings.IsRTL ? "\uF2C8" : "\uF2C7", FontSize = 16 } },
        { ThumbnailService.ThumbnailSize.Medium, new FontIcon() { Glyph = "\uE138", FontSize = 16 } },
        { ThumbnailService.ThumbnailSize.Large, new LargeThumbsIcon() { SubFontSize = 8 } },
        { ThumbnailService.ThumbnailSize.ExtraLarge, new FontIcon() { Glyph = "\uE15A", FontSize = 16 } },
    };

    static Dictionary<AppSettings.SidePaneMode, UIElement> SidePaneModeIcons => new()
    {
        { AppSettings.SidePaneMode.Details, new FontIcon()
        {
            Glyph = "\uE99C",
            FontSize = 16,
            RenderTransformOrigin = new(0.5, 0.5),
            RenderTransform = Data.RuntimeSettings.IsRTL ? null : new ScaleTransform(-1, 1)
        } },
        { AppSettings.SidePaneMode.Preview, new FontIcon()
        {
            Glyph = "\uE1AC",
            FontSize = 16,
            RenderTransformOrigin = new(0.5, 0.5),
            RenderTransform = Data.RuntimeSettings.IsRTL ? null : new ScaleTransform(-1, 1)
        } },
    };

    public ICollection<object> Items { get; }

    public UIElement SelectedIcon => Icons[ThumbnailSize];

    public void SetThumbnailSize(ThumbnailService.ThumbnailSize size)
    {
        ThumbnailSize = size;
    }

    public ThumbnailService.ThumbnailSize ThumbnailSize
    {
        get => (ThumbnailService.ThumbnailSize)GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbnailSizeProperty =
        DependencyProperty.Register(nameof(ThumbnailSize), typeof(ThumbnailService.ThumbnailSize),
          typeof(ThumbsSizeSelector), new PropertyMetadata(ThumbnailService.ThumbnailSize.Disabled, OnThumbnailSizePropertyChanged));

    private static void OnThumbnailSizePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (ThumbsSizeSelector)d;
        selector.OnPropertyChanged(nameof(SelectedIcon));
        selector.OnPropertyChanged(nameof(ThumbnailSize));
    }

    public partial class SidePaneModeItem : ObservableObject
    {
        public string Name { get; set; }
        public UIElement Icon { get; set; }

        [ObservableProperty]
        public partial bool IsChecked { get; set; } = false;

        public BaseAction Action { get; set; }

        public SidePaneModeItem(string name, AppSettings.SidePaneMode mode)
        {
            Name = name;
            Icon = SidePaneModeIcons[mode];
            Action = new(() => !Data.FileActions.IsRecycleBin, () => Data.Settings.SidePane = mode);
            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SidePane))
                {
                    IsChecked = Data.Settings.SidePane == mode;
                }
            };

            IsChecked = Data.Settings.SidePane == mode;
        }
    }

    public partial class ThumbSizeItem : ObservableObject
    {
        public string Name { get; set; }
        public UIElement Icon { get; set; }

        [ObservableProperty]
        public partial bool IsChecked { get; set; } = false;

        public BaseAction Action { get; set; }

        public ThumbSizeItem(string name, ThumbnailService.ThumbnailSize size, ThumbsSizeSelector selector)
        {
            Name = name;
            Icon = Icons[size];
            Action = new(() => true, () => selector.SetThumbnailSize(size));
            selector.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThumbnailSize))
                {
                    IsChecked = selector.ThumbnailSize == size;
                }
            };

            IsChecked = selector.ThumbnailSize == size;
        }
    }
}
