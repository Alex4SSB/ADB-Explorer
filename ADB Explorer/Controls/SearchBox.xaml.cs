using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SearchBox.xaml
/// </summary>
public partial class SearchBox : UserControl
{
    public SearchBox()
    {
        InitializeComponent();

        IsEnabledChanged += (sender, e) =>
        {
            if (!IsEnabled)
                Expander.IsExpanded = false;
        };

        Data.UnfocusSearchBox += (s, e) => Unfocus();
    }

    private void Unfocus()
    {
        if (!IsKeyboardFocusWithin)
            return;

        UnfocusTarget?.Focus();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string),
          typeof(SearchBox), new PropertyMetadata(null));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register("PlaceholderText", typeof(string),
          typeof(SearchBox), new PropertyMetadata(null));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register("IsActive", typeof(bool),
          typeof(SearchBox), new PropertyMetadata(null));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty); 
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register("IsExpanded", typeof(bool),
          typeof(SearchBox), new PropertyMetadata(null));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register("Icon", typeof(string),
          typeof(SearchBox), new PropertyMetadata(null));

    public bool IsFiltered
    {
        get => (bool)GetValue(IsFilteredProperty);
        protected set => SetValue(IsFilteredProperty, value);
    }

    public static readonly DependencyProperty IsFilteredProperty =
        DependencyProperty.Register("IsFiltered", typeof(bool),
          typeof(SearchBox), new PropertyMetadata(null));

    public double MaxControlWidth
    {
        get => (double)GetValue(MaxControlWidthProperty);
        set => SetValue(MaxControlWidthProperty, value);
    }

    public static readonly DependencyProperty MaxControlWidthProperty =
        DependencyProperty.Register("MaxControlWidth", typeof(double),
          typeof(SearchBox), new PropertyMetadata(null));

    public double MinControlWidth
    {
        get => (double)GetValue(MinControlWidthProperty);
        set => SetValue(MinControlWidthProperty, value);
    }

    public static readonly DependencyProperty MinControlWidthProperty =
        DependencyProperty.Register("MinControlWidth", typeof(double),
          typeof(SearchBox), new PropertyMetadata(null));

    public double DefaultControlWidth
    {
        get => (double)GetValue(DefaultControlWidthProperty);
        set => SetValue(DefaultControlWidthProperty, value);
    }

    public static readonly DependencyProperty DefaultControlWidthProperty =
        DependencyProperty.Register("DefaultControlWidth", typeof(double),
          typeof(SearchBox), new PropertyMetadata(null));

    public UIElement? UnfocusTarget
    {
        get => (UIElement?)GetValue(UnfocusTargetProperty);
        set => SetValue(UnfocusTargetProperty, value);
    }

    public static readonly DependencyProperty UnfocusTargetProperty =
        DependencyProperty.Register(nameof(UnfocusTarget), typeof(UIElement),
          typeof(SearchBox), new PropertyMetadata(null));

    public void Refresh()
    {
        if (ContentBox.ActualWidth > MaxControlWidth)
            ContentBox.Width = MaxControlWidth;
        else if (ContentBox.ActualWidth < MinControlWidth)
            ContentBox.Width = MinControlWidth;
    }

    /// <summary>Raised on Enter (commit) or Escape (clear).</summary>
    public event RoutedEventHandler? Committed;

    private void ContentBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            Text = "";
            Unfocus();
            Committed?.Invoke(this, new RoutedEventArgs());
        }
        else if (e.Key is Key.Enter)
        {
            Committed?.Invoke(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((ContentBox.ActualWidth > MinControlWidth && e.HorizontalChange > 0)
            || (ContentBox.ActualWidth < MaxControlWidth && e.HorizontalChange < 0))
            ContentBox.Width = ContentBox.ActualWidth - e.HorizontalChange;
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        IsFiltered = !string.IsNullOrEmpty(Text);
    }

    private void ContentBox_Loaded(object sender, RoutedEventArgs e)
    {
        ContentBox.Width = DefaultControlWidth;
    }

    private void Expander_Expanded(object sender, RoutedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ContentBox.Focus();
            Keyboard.Focus(ContentBox);
        }, DispatcherPriority.Input);
    }
}
