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
        InitializeComponent();

        Data.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Data.Settings.ThumbsSize))
            {
                OnPropertyChanged(nameof(SelectedIcon));
            }
        };

        Data.FileActions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Data.FileActions.IsAppDrive))
            {
                OnPropertyChanged(nameof(SelectedIcon));
            }
        };
    }

    static UIElement[] Icons =>
        [
        new FontIcon() { Glyph = "\uF2C7", FontSize = 16 },
        new FontIcon() { Glyph = "\uE138", FontSize = 16 },
        new LargeThumbsIcon() { SubFontSize = 8 },
        new FontIcon() { Glyph = "\uE15A", FontSize = 16 },
    ];

    public ICollection<ThumbSizeItem> Items { get; } =
    [
        new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_DETAILS, Icons[0], ThumbnailService.ThumbnailSize.Disabled),
        new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_MEDIUM, Icons[1], ThumbnailService.ThumbnailSize.Medium),
        new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_LARGE, Icons[2], ThumbnailService.ThumbnailSize.Large),
        new ThumbSizeItem(Strings.Resources.S_THUMBSIZE_XL, Icons[3], ThumbnailService.ThumbnailSize.ExtraLarge),
    ];

    public UIElement SelectedIcon
    {
        get
        {
            if (Data.FileActions.IsAppDrive)
            {
                return Icons[0];
            }

            return Data.Settings.ThumbsSize switch
            {
                ThumbnailService.ThumbnailSize.Disabled => Icons[0],
                ThumbnailService.ThumbnailSize.Medium => Icons[1],
                ThumbnailService.ThumbnailSize.Large => Icons[2],
                ThumbnailService.ThumbnailSize.ExtraLarge => Icons[3],
                _ => null,
            };
        }
    }

    public partial class ThumbSizeItem : ObservableObject
    {
        public string Name { get; set; }
        public UIElement Icon { get; set; }

        [ObservableProperty]
        public partial bool IsChecked { get; set; } = false;

        public BaseAction Action { get; set; }

        public ThumbSizeItem(string name, UIElement icon, ThumbnailService.ThumbnailSize size)
        {
            Name = name;
            Icon = icon;
            Action = new(() => true, () => Data.Settings.ThumbsSize = size);

            Data.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Data.Settings.ThumbsSize))
                {
                    IsChecked = Data.Settings.ThumbsSize == size;
                }
            };

            IsChecked = Data.Settings.ThumbsSize == size;
        }
    }
}
