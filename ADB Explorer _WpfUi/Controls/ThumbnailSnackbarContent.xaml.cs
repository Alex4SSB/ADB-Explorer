namespace ADB_Explorer.Controls;

public partial class ThumbnailSnackbarContent : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ThumbnailSnackbarContent), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ThumbnailSnackbarContent), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ThumbnailSnackbarContent), new PropertyMetadata(0.0));

    public static readonly DependencyProperty CounterTextProperty =
        DependencyProperty.Register(nameof(CounterText), typeof(string), typeof(ThumbnailSnackbarContent), new PropertyMetadata("0 / 0"));

    public static readonly DependencyProperty ShowProgressProperty =
        DependencyProperty.Register(nameof(ShowProgress), typeof(bool), typeof(ThumbnailSnackbarContent), new PropertyMetadata(false));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string CounterText
    {
        get => (string)GetValue(CounterTextProperty);
        set => SetValue(CounterTextProperty, value);
    }

    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public ThumbnailSnackbarContent()
    {
        InitializeComponent();
    }
}
