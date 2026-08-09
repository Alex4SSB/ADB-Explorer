using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
        Type,
        UserId,
        Version,
    }

    public SortingSelector()
    {
        Items = [];
        RebuildItems();

        InitializeComponent();

        Data.FileActions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive))
                RebuildItems();
        };
    }

    public ObservableCollection<object> Items { get; }

    public void RebuildItems()
    {
        foreach (var item in Items.OfType<SortingSelectorItem>())
            item.Detach();

        Items.Clear();

        if (Data.FileActions.IsAppDrive)
        {
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_NAME, SortingProperty.Name, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_TYPE, SortingProperty.Type, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_USER_ID, SortingProperty.UserId, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_VERSION, SortingProperty.Version, this));
        }
        else
        {
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_NAME, SortingProperty.Name, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_DATE_MODIFIED, SortingProperty.Date, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_SIZE, SortingProperty.Size, this));
            Items.Add(new SortingSelectorItem(Strings.Resources.S_COLUMN_TYPE, SortingProperty.Type, this));
        }

        Items.Add(new Separator());
        Items.Add(new SortingSelectorItem(Strings.Resources.S_SORT_ASCENDING, ListSortDirection.Ascending, this));
        Items.Add(new SortingSelectorItem(Strings.Resources.S_SORT_DESCENDING, ListSortDirection.Descending, this));
    }

    public void SetSortDirection(ListSortDirection direction)
    {
        SortDirection = direction;
    }

    public void SetSortOption(SortingProperty option)
    {
        SortOption = option;
    }

    public ListSortDirection? SortDirection
    {
        get => (ListSortDirection?)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }

    public static readonly DependencyProperty SortDirectionProperty =
        DependencyProperty.Register(nameof(SortDirection), typeof(ListSortDirection?),
          typeof(SortingSelector), new PropertyMetadata(ListSortDirection.Ascending, OnSortDirectionPropertyChanged));

    private static void OnSortDirectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SortingSelector)d;
        selector.OnPropertyChanged(nameof(SortDirection));
    }

    public SortingProperty? SortOption
    {
        get => (SortingProperty?)GetValue(SortOptionProperty);
        set => SetValue(SortOptionProperty, value);
    }

    public static readonly DependencyProperty SortOptionProperty =
        DependencyProperty.Register(nameof(SortOption), typeof(SortingProperty?),
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

        private readonly SortingSelector _selector;
        private readonly PropertyChangedEventHandler _handler;

        public SortingSelectorItem(string name, SortingProperty prop, SortingSelector selector)
        {
            Name = name;
            _selector = selector;
            Action = new(() => true, () => selector.SetSortOption(prop));
            _handler = (_, e) =>
            {
                if (e.PropertyName == nameof(SortOption))
                    IsChecked = selector.SortOption == prop;
            };
            selector.PropertyChanged += _handler;
            IsChecked = selector.SortOption == prop;
        }

        public SortingSelectorItem(string name, ListSortDirection direction, SortingSelector selector)
        {
            Name = name;
            _selector = selector;
            Action = new(() => true, () => selector.SetSortDirection(direction));
            _handler = (_, e) =>
            {
                if (e.PropertyName == nameof(SortDirection))
                    IsChecked = selector.SortDirection == direction;
            };
            selector.PropertyChanged += _handler;
            IsChecked = selector.SortDirection == direction;
        }

        public void Detach()
        {
            if (_handler is not null)
                _selector.PropertyChanged -= _handler;
        }
    }
}
