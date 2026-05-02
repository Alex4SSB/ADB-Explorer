using ADB_Explorer.Helpers;
using Newtonsoft.Json;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SortingSelector.xaml
/// </summary>
[ObservableObject]
public partial class SortingSelector : UserControl
{
    public enum SortingProperty
    {
        Name,
        Date,
        Size,
        Type
    }

    public SortingSelector()
    {
        Items = [
            new SortingSelectorItem(Strings.Resources.S_COLUMN_NAME, SortingProperty.Name, this),
            new SortingSelectorItem(Strings.Resources.S_COLUMN_DATE_MODIFIED, SortingProperty.Date, this),
            new SortingSelectorItem(Strings.Resources.S_COLUMN_SIZE, SortingProperty.Size, this),
            new SortingSelectorItem(Strings.Resources.S_COLUMN_TYPE, SortingProperty.Type, this),
            new Separator(),
            new SortingSelectorItem(Strings.Resources.S_SORT_ASCENDING, ListSortDirection.Ascending, this),
            new SortingSelectorItem(Strings.Resources.S_SORT_DESCENDING, ListSortDirection.Descending, this)
        ];

        InitializeComponent();
    }
    
    public ICollection<object> Items { get; }

    public void SetSortDirection(ListSortDirection direction)
    {
        SortDirection = direction;
    }

    public void SetSortOption(SortingProperty option)
    {
        SortOption = option;
    }

    public ListSortDirection SortDirection
    {
        get => (ListSortDirection)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }

    public static readonly DependencyProperty SortDirectionProperty =
        DependencyProperty.Register(nameof(SortDirection), typeof(ListSortDirection),
          typeof(SortingSelector), new PropertyMetadata(ListSortDirection.Ascending, OnSortDirectionPropertyChanged));

    private static void OnSortDirectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SortingSelector)d;
        selector.OnPropertyChanged(nameof(SortDirection));
    }

    public SortingProperty SortOption
    {
        get => (SortingProperty)GetValue(SortOptionProperty);
        set => SetValue(SortOptionProperty, value);
    }

    public static readonly DependencyProperty SortOptionProperty =
        DependencyProperty.Register(nameof(SortOption), typeof(SortingProperty),
          typeof(SortingSelector), new PropertyMetadata(SortingProperty.Name, OnSortOptionPropertyChanged));

    private static void OnSortOptionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SortingSelector)d;
        selector.OnPropertyChanged(nameof(SortOption));
    }

    public record struct DirSortingOption(SortingProperty Property, ListSortDirection Direction);

    public partial class SortingSelectorItem : ObservableObject
    {
        public string Name { get; set; }

        [ObservableProperty]
        public partial bool IsChecked { get; set; } = false;

        public BaseAction Action { get; set; }

        public SortingSelectorItem(string name, SortingProperty prop, SortingSelector selector)
        {
            Name = name;
            Action = new(() => true, () => selector.SetSortOption(prop));
            selector.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SortOption))
                {
                    IsChecked = selector.SortOption == prop;
                }
            };

            IsChecked = selector.SortOption == prop;
        }

        public SortingSelectorItem(string name, ListSortDirection direction, SortingSelector selector)
        {
            Name = name;
            Action = new(() => true, () => selector.SetSortDirection(direction));
            selector.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SortDirection))
                {
                    IsChecked = selector.SortDirection == direction;
                }
            };

            IsChecked = selector.SortDirection == direction;
        }
    }
}
