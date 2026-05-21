using ADB_Explorer.Services;

namespace ADB_Explorer.Controls.ColorPicker;

/// <summary>
/// A color swatch button that opens the full color picker inside a ContentDialog.
/// Exposes a single <see cref="SelectedColor"/> dependency property.
/// </summary>
public partial class ColorPickerControl : UserControl
{
    private SolidColorBrush _swatchBrush = null!;

    // ─── Dependency Property ──────────────────────────────────────────────────
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorPickerControl),
            new FrameworkPropertyMetadata(
                Colors.Red,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (ColorPickerControl)d;
        ctrl._swatchBrush.Color = (Color)e.NewValue;
    }

    // ─── Constructor ──────────────────────────────────────────────────────────
    public ColorPickerControl()
    {
        InitializeComponent();
        _swatchBrush = (SolidColorBrush)FindName("SwatchBrush");
        _swatchBrush.Color = SelectedColor;
    }

    // ─── Swatch Button Click ──────────────────────────────────────────────────
    private async void SwatchButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new ColorPickerPanel
        {
            SelectedColor = SelectedColor
        };

        var result = await DialogService.ShowDialog(
            AdbContentDialog.CustomContentDialog(panel),
            Strings.Resources.S_PICK_COLOR,
            primaryText: Strings.Resources.S_CONFIRM,
            closeText: Strings.Resources.S_CANCEL);

        if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
            SelectedColor = panel.SelectedColor;
    }
}
