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
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_XL, ThumbnailService.ThumbnailSize.ExtraLarge, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_LARGE, ThumbnailService.ThumbnailSize.Large, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_MEDIUM, ThumbnailService.ThumbnailSize.Medium, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_DETAILS, ThumbnailService.ThumbnailSize.Disabled, this),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_TILES, ThumbnailService.ThumbnailSize.Tiles, this, canSelect: false),
            new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_CONTENT, ThumbnailService.ThumbnailSize.Content, this),
            new Separator(),
            new SidePaneModeItem(Strings.Resources.S_THUMBSIZE_DETAILS, SidePaneMode.Details, Strings.Resources.S_DETAILS_PANE_INFO),
            new SidePaneModeItem(Strings.Resources.S_SIDE_PANE_PREVIEW, SidePaneMode.Preview, Strings.Resources.S_PREVIEW_PANE_INFO),
            new Separator(),
            new SelectorItem() {
                Name = Strings.Resources.S_MENU_SHOW,
                Icon = new FontIcon() { Glyph = "\uE138", FontSize = 16, Visibility = Visibility.Hidden },
                Children = [
                    new SettingToggleItem(Strings.Resources.S_SETTINGS_SHOW_EXTENSIONS, () => Data.Settings.ShowExtensions, new FontIcon() { Glyph = "\uE8AC", FontSize = 16 }),
                    new SettingToggleItem(Strings.Resources.S_SETTINGS_HIDDEN_ITEMS, () => Data.Settings.ShowHiddenItems, new FontIcon() { Glyph = "\uE8FF", FontSize = 16 })
                    ]
            },
        ];

        InitializeComponent();
    }

    /// <summary>A new icon element for the size - one element can't have two parents.</summary>
    internal static UIElement CreateIcon(ThumbnailService.ThumbnailSize size) => size switch
    {
        ThumbnailService.ThumbnailSize.Content => (UIElement)new BaseIcon("\uE71D", 16, rtlBehavior: RtlBehavior.FlipInRtl).IconContent,
        ThumbnailService.ThumbnailSize.Tiles => new FluentPathIcon() { Data = FluentPathGeometries.AppsListDetail, Height = 16 },
        ThumbnailService.ThumbnailSize.Disabled => new TextJustify() { Size = 16 },
        ThumbnailService.ThumbnailSize.Medium => (UIElement)new BaseIcon("\uE138", 16).IconContent,
        ThumbnailService.ThumbnailSize.Large => new LargeThumbsIcon() { SubFontSize = 8 },
        ThumbnailService.ThumbnailSize.ExtraLarge => (UIElement)new BaseIcon("\uE15A", 16, rtlBehavior: RtlBehavior.FlipInRtl).IconContent,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    static Dictionary<SidePaneMode, UIElement> SidePaneModeIcons => new()
    {
        { SidePaneMode.Details, new DetailsAndPreviewIcon()
        {
            Size = 16,
            Mode = SidePaneMode.Details,
            Stretch = Stretch.Uniform,
        } },
        { SidePaneMode.Preview, new DetailsAndPreviewIcon()
        {
            Size = 16,
            Mode = SidePaneMode.Preview,
            Stretch = Stretch.Uniform,
        } },
    };

    public ICollection<object> Items { get; }

    public UIElement SelectedIcon => CreateIcon(ThumbnailSize);

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

    public class SidePaneModeItem : SelectorItem
    {
        private readonly SidePaneMode _mode;

        public SidePaneModeItem(string name, SidePaneMode mode, string? info = null)
        {
            _mode = mode;
            Info = info;
            Name = name;
            Icon = SidePaneModeIcons[mode];
            Action = new(() => Data.FileActions.IsPreviewAllowed, () => Data.Settings.SidePane = mode);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.SidePane))
                    Refresh();
            };

            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive) or nameof(FileActionsEnable.IsRecycleBin) or nameof(FileActionsEnable.IsExplorerVisible))
                    Refresh();
            };

            Refresh();
        }

        private void Refresh() => IsChecked = Data.FileActions.IsPreviewAllowed
            ? Data.Settings.SidePane == _mode
            : _mode == SidePaneMode.Details;
    }

    public class ThumbSizeItem : SelectorItem
    {
        public ThumbSizeItem(string name, ThumbnailService.ThumbnailSize size, ThumbsSizeSelector selector, bool canSelect = true)
        {
            Name = name;
            Icon = CreateIcon(size);
            if (canSelect)
                Action = new(IsThumbSizeChangeAllowed, () => selector.SetThumbnailSize(size));
            else
                Action = new(() => false, static () => { });

            selector.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThumbnailSize))
                {
                    IsChecked = selector.ThumbnailSize == size;
                }
            };

            IsChecked = selector.ThumbnailSize == size;
        }

        internal static bool IsThumbSizeChangeAllowed() =>
            Data.Settings.ThumbsMode > AppSettings.ThumbnailMode.Off 
            && !Data.FileActions.IsAppDriveThumbsLocked
            && Data.FileActions.IsExplorerVisible;
    }
}
