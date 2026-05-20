namespace ADB_Explorer.Controls.ColorPicker;

/// <summary>
/// The picker grid content hosted inside a ContentDialog.
/// Exposes a <see cref="SelectedColor"/> dependency property that is kept
/// in sync with its owner <see cref="ColorPickerControl"/>.
/// </summary>
public partial class ColorPickerPanel : UserControl
{
    // ─── HSV state ────────────────────────────────────────────────────────────
    private double _hue = 0;
    private double _sat = 1;
    private double _val = 1;

    private bool _updating = false;

    private SolidColorBrush _previewBrush = null!;
    private GradientStop _valueTopGradientStop = null!;

    // ─── Dependency Property ──────────────────────────────────────────────────
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorPickerPanel),
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
        var ctrl = (ColorPickerPanel)d;
        if (!ctrl._updating)
            ctrl.UpdateFromRgb((Color)e.NewValue, updateDp: false);
    }

    // ─── Constructor ──────────────────────────────────────────────────────────
    public ColorPickerPanel()
    {
        InitializeComponent();

        _previewBrush           = (SolidColorBrush)FindName("PreviewBrush");
        _valueTopGradientStop   = (GradientStop)FindName("ValueTopGradientStop");
    }

    // ─── Spectrum Canvas drag ─────────────────────────────────────────────────
    private void SpectrumCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        ((UIElement)sender).CaptureMouse();
        UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
    }

    private void SpectrumCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
    }

    private void SpectrumCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void UpdateSpectrumFromPoint(Point p)
    {
        double w = SpectrumCanvas.ActualWidth;
        double h = SpectrumCanvas.ActualHeight;

        _hue = Math.Clamp(p.X / w, 0, 1) * 360;
        _sat = Math.Clamp(1 - p.Y / h, 0, 1);

        UpdateFromHsv();
    }

    // ─── Value Bar drag ────────────────────────────────────────────────────────
    private void ValueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        ((UIElement)sender).CaptureMouse();
        UpdateValueFromPoint(e.GetPosition(ValueCanvas));
    }

    private void ValueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            UpdateValueFromPoint(e.GetPosition(ValueCanvas));
    }

    private void ValueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void UpdateValueFromPoint(Point p)
    {
        double h = ValueCanvas.ActualHeight;
        _val = Math.Clamp(1 - p.Y / h, 0, 1);
        UpdateFromHsv();
    }

    // ─── Hex TextBox ──────────────────────────────────────────────────────────
    private void HexTextBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    private void CommitHex()
    {
        if (_updating) return;
        var text = HexTextBox.Text.TrimStart('#');
        if (text.Length == 6 && TryParseHex(text, out var color))
            UpdateFromRgb(color);
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Black;
        try
        {
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            color = Color.FromRgb(r, g, b);
            return true;
        }
        catch { return false; }
    }

    // ─── Channel TextBoxes ────────────────────────────────────────────────────
    private void ChannelBox_LostFocus(object sender, RoutedEventArgs e) => CommitChannels();

    private void ChannelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitChannels();
    }

    private void CommitChannels()
    {
        if (_updating) return;

        bool isHsv = ModeComboBox.SelectedIndex == 1;

        if (!double.TryParse(Ch1Box.Text, out double v1)) return;
        if (!double.TryParse(Ch2Box.Text, out double v2)) return;
        if (!double.TryParse(Ch3Box.Text, out double v3)) return;

        if (isHsv)
        {
            _hue = Math.Clamp(v1, 0, 360);
            _sat = Math.Clamp(v2, 0, 100) / 100.0;
            _val = Math.Clamp(v3, 0, 100) / 100.0;
            UpdateFromHsv();
        }
        else
        {
            byte r = (byte)Math.Clamp((int)v1, 0, 255);
            byte g = (byte)Math.Clamp((int)v2, 0, 255);
            byte b = (byte)Math.Clamp((int)v3, 0, 255);
            UpdateFromRgb(Color.FromRgb(r, g, b));
        }
    }

    // ─── Mode ComboBox ────────────────────────────────────────────────────────
    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Ch1Label is null) return;

        bool isHsv = ModeComboBox.SelectedIndex == 1;
        Ch1Label.Text = isHsv ? "Hue" : "Red";
        Ch2Label.Text = isHsv ? "Saturation" : "Green";
        Ch3Label.Text = isHsv ? "Value" : "Blue";

        RefreshChannelBoxes();
    }

    // ─── Core update methods ──────────────────────────────────────────────────
    private void UpdateFromHsv()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var color = HsvToRgb(_hue, _sat, _val);

            SelectedColor = color;
            _previewBrush.Color = color;

            // Value bar top = full-value color at current hue+sat
            _valueTopGradientStop.Color = HsvToRgb(_hue, _sat, 1);

            double sw = SpectrumCanvas.ActualWidth  > 0 ? SpectrumCanvas.ActualWidth  : 200;
            double sh = SpectrumCanvas.ActualHeight > 0 ? SpectrumCanvas.ActualHeight : 200;
            // X = hue/360, Y = 1−sat (sat=1 at top)
            Canvas.SetLeft(SpectrumThumb, (_hue / 360.0) * sw - SpectrumThumb.Width  / 2);
            Canvas.SetTop (SpectrumThumb, (1 - _sat)     * sh - SpectrumThumb.Height / 2);

            double vh = ValueCanvas.ActualHeight > 0 ? ValueCanvas.ActualHeight : 200;
            Canvas.SetTop(ValueThumb, (1 - _val) * vh - ValueThumb.Height / 2);

            HexTextBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            RefreshChannelBoxes();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateFromRgb(Color color, bool updateDp = true)
    {
        if (_updating) return;
        RgbToHsv(color, out _hue, out _sat, out _val);

        if (updateDp)
        {
            UpdateFromHsv();
        }
        else
        {
            _updating = true;
            try
            {
                _previewBrush.Color = color;
                _valueTopGradientStop.Color = HsvToRgb(_hue, _sat, 1);

                double sw = SpectrumCanvas.ActualWidth  > 0 ? SpectrumCanvas.ActualWidth  : 200;
                double sh = SpectrumCanvas.ActualHeight > 0 ? SpectrumCanvas.ActualHeight : 200;
                Canvas.SetLeft(SpectrumThumb, (_hue / 360.0) * sw - SpectrumThumb.Width  / 2);
                Canvas.SetTop (SpectrumThumb, (1 - _sat)     * sh - SpectrumThumb.Height / 2);

                double vh = ValueCanvas.ActualHeight > 0 ? ValueCanvas.ActualHeight : 200;
                Canvas.SetTop(ValueThumb, (1 - _val) * vh - ValueThumb.Height / 2);

                HexTextBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                RefreshChannelBoxes();
            }
            finally
            {
                _updating = false;
            }
        }
    }

    private void RefreshChannelBoxes()
    {
        bool isHsv = ModeComboBox?.SelectedIndex == 1;
        if (isHsv)
        {
            Ch1Box.Text = $"{_hue:F0}";
            Ch2Box.Text = $"{_sat * 100:F0}";
            Ch3Box.Text = $"{_val * 100:F0}";
        }
        else
        {
            var c = HsvToRgb(_hue, _sat, _val);
            Ch1Box.Text = c.R.ToString();
            Ch2Box.Text = c.G.ToString();
            Ch3Box.Text = c.B.ToString();
        }
    }

    // ─── HSV ↔ RGB math ───────────────────────────────────────────────────────
    private static Color HsvToRgb(double hue, double sat, double val)
    {
        if (sat <= 0)
        {
            byte k = (byte)Math.Round(val * 255);
            return Color.FromRgb(k, k, k);
        }

        double h = hue / 60.0;
        int i = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);

        double p = val * (1 - sat);
        double q = val * (1 - sat * f);
        double t = val * (1 - sat * (1 - f));

        (double r, double g, double b) = i switch
        {
            0 => (val, t, p),
            1 => (q, val, p),
            2 => (p, val, t),
            3 => (p, q, val),
            4 => (t, p, val),
            _ => (val, p, q),
        };

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double sat, out double val)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        val = max;
        sat = max < 1e-9 ? 0 : delta / max;

        if (delta < 1e-9)
        {
            hue = 0;
            return;
        }

        if (max == r)
            hue = 60.0 * (((g - b) / delta) % 6);
        else if (max == g)
            hue = 60.0 * ((b - r) / delta + 2);
        else
            hue = 60.0 * ((r - g) / delta + 4);

        if (hue < 0) hue += 360;
    }
}
