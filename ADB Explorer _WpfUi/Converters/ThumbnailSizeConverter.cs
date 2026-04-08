using ADB_Explorer.Services;

namespace ADB_Explorer.Converters;

/// <summary>
/// Converts a <see cref="ThumbnailService.ThumbnailSize"/> to a <see cref="double"/> dimension value.
/// The converter parameter specifies which dimension: ContainerWidth, ContainerHeight, ImageMaxWidth, ImageMaxHeight, LabelMaxHeight, EditWidth, OverlayMaxWidth.
/// </summary>
public class ThumbnailSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ThumbnailService.ThumbnailSize size || parameter is not string dim)
            return DependencyProperty.UnsetValue;

        return dim switch
        {
            "ContainerWidth" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 80d,
                ThumbnailService.ThumbnailSize.Large => 116d,
                _ => 210d,
            },
            "ContainerHeight" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 126d,
                ThumbnailService.ThumbnailSize.Large => 142d,
                _ => 230d,
            },
            "ImageMaxWidth" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 60d,
                ThumbnailService.ThumbnailSize.Large => 96d,
                _ => 192d,
            },
            "ImageMaxHeight" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 55d,
                ThumbnailService.ThumbnailSize.Large => 96d,
                _ => 170d,
            },
            "LabelMaxHeight" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 70d,
                ThumbnailService.ThumbnailSize.Large => 54d,
                _ => 60d,
            },
            "EditWidth" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 80d,
                ThumbnailService.ThumbnailSize.Large => 116d,
                _ => 210d,
            },
            "OverlayMaxWidth" => size switch
            {
                ThumbnailService.ThumbnailSize.Medium => 50d,
                ThumbnailService.ThumbnailSize.Large => 80d,
                _ => 160d,
            },
            _ => DependencyProperty.UnsetValue,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
