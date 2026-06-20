using Wpf.Ui.Controls;

namespace ADB_Explorer.Converters;

/// <summary>
/// Maps WPF UI <see cref="ControlAppearance"/> to card background or border brushes for settings cards.
/// </summary>
public class ControlAppearanceToCardBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ControlAppearance appearance)
            return DependencyProperty.UnsetValue;

        var useBorder = string.Equals(parameter as string, "Border", StringComparison.OrdinalIgnoreCase);
        var resourceKey = appearance switch
        {
            ControlAppearance.Danger => "SystemFillColorCriticalBrush",
            ControlAppearance.Caution => "SystemFillColorCautionBrush",
            ControlAppearance.Success => "SystemFillColorSuccessBrush",
            ControlAppearance.Info => "SystemFillColorAttentionBrush",
            ControlAppearance.Primary => "SystemAccentColorBrush",
            ControlAppearance.Secondary => "ControlStrokeColorDefaultBrush",
            _ => null,
        };

        if (resourceKey is null)
            return DependencyProperty.UnsetValue;

        if (Application.Current?.Resources[resourceKey] is not SolidColorBrush source)
            return DependencyProperty.UnsetValue;

        if (useBorder)
            return source;

        return new SolidColorBrush(source.Color) { Opacity = 0.12 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
