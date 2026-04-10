using ADB_Explorer.Helpers;
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
        ];

        InitializeComponent();
    }

    static Dictionary<ThumbnailService.ThumbnailSize, UIElement> Icons => new()
    {
        { ThumbnailService.ThumbnailSize.Disabled, new FontIcon() { Glyph = "\uF2C7", FontSize = 16 } },
        { ThumbnailService.ThumbnailSize.Medium, new FontIcon() { Glyph = "\uE138", FontSize = 16 } },
        { ThumbnailService.ThumbnailSize.Large, new LargeThumbsIcon() { SubFontSize = 8 } },
        { ThumbnailService.ThumbnailSize.ExtraLarge, new FontIcon() { Glyph = "\uE15A", FontSize = 16 } },
    };
    
    public ICollection<ThumbSizeItem> Items { get; }

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
