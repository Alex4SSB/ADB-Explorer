using ADB_Explorer.Models;
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

        // In HC, Danger/Caution cards use the same inverted title-bar accent as the reset-settings
        // button (see SettingsPageHeader.xaml's own comment on that button) instead of
        // SystemFillColorCritical/CautionBrush — faded the same 0.12 opacity as every other card's
        // idle fill below. Border and the HC hover trigger (SettingsCardHeaderStyle/
        // SettingsCardExpanderStyle) are untouched, so hover stays exactly as it already was.
        if (!useBorder
            && Data.RuntimeSettings.IsHighContrast
            && appearance is ControlAppearance.Danger or ControlAppearance.Caution
            && Application.Current?.Resources["TitleBarBackgroundBrush"] is SolidColorBrush hcSource)
        {
            return new SolidColorBrush(hcSource.Color) { Opacity = 0.12 };
        }

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
