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
            new SidePaneModeItem(Strings.Resources.S_THUMBSIZE_DETAILS, DetailsPane.SidePaneMode.Details),
            new SidePaneModeItem(Strings.Resources.S_SIDE_PANE_PREVIEW, DetailsPane.SidePaneMode.Preview),
        ];

        InitializeComponent();
    }

    static Dictionary<ThumbnailService.ThumbnailSize, UIElement> Icons => new()
    {
        { ThumbnailService.ThumbnailSize.Disabled, new FluentPathIcon() { Data = FluentPathGeometries.TextBulletList, Height = 16 } },
        { ThumbnailService.ThumbnailSize.Medium, new FontIcon() { Glyph = "\uE138", FontSize = 16 } },
        { ThumbnailService.ThumbnailSize.Large, new LargeThumbsIcon() { SubFontSize = 8 } },
        { ThumbnailService.ThumbnailSize.ExtraLarge, new FontIcon() { Glyph = "\uE15A", FontSize = 16 } },
    };

    static Dictionary<DetailsPane.SidePaneMode, UIElement> SidePaneModeIcons => new()
    {
        { DetailsPane.SidePaneMode.Details, new DetailsAndPreviewIcon()
        {
            Size = 16,
            Mode = DetailsPane.SidePaneMode.Details,
            Stretch = Stretch.Uniform,
        } },
        { DetailsPane.SidePaneMode.Preview, new DetailsAndPreviewIcon()
        {
            Size = 16,
            Mode = DetailsPane.SidePaneMode.Preview,
            Stretch = Stretch.Uniform,
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

        public SidePaneModeItem(string name, DetailsPane.SidePaneMode mode)
        {
            Name = name;
            Icon = SidePaneModeIcons[mode];
            Action = new(IsPreviewAllowed, () => Data.Settings.SidePane = mode);
            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SidePane))
                {
                    IsChecked = IsPreviewAllowed()
                        ? Data.Settings.SidePane == mode
                        : mode == DetailsPane.SidePaneMode.Details;
                }
            };
            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsDriveViewVisible) or nameof(FileActionsEnable.IsAppDrive) or nameof(FileActionsEnable.IsRecycleBin))
                {
                    IsChecked = IsPreviewAllowed()
                        ? Data.Settings.SidePane == mode
                        : mode == DetailsPane.SidePaneMode.Details;
                }
            };

            IsChecked = IsPreviewAllowed()
                ? Data.Settings.SidePane == mode
                : mode == DetailsPane.SidePaneMode.Details;
        }

        private static bool IsPreviewAllowed() => !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive && !Data.FileActions.IsDriveViewVisible;
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
            Action = new(() => Data.Settings.ThumbsMode > AppSettings.ThumbnailMode.Off, () => selector.SetThumbnailSize(size));
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
