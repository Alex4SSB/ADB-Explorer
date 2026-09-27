namespace ADB_Explorer.Controls;

/// <summary>
/// Two arrows with the one for the active sort direction bold and accented; scales to whatever
/// size its host gives it, so the toolbar and the context menu share it.
/// </summary>
public partial class SortDirectionIcon : UserControl
{
    public SortDirectionIcon()
    {
        InitializeComponent();
    }

    public ListSortDirection? Direction
    {
        get => (ListSortDirection?)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(ListSortDirection?),
          typeof(SortDirectionIcon), new PropertyMetadata(null));
}
