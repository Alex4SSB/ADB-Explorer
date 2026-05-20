using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

public record FileOpDeviceGroup(string DeviceName, IList<FileOperation> Operations);

public partial class FileOpSnackbarContent : UserControl
{
    public static readonly DependencyProperty OperationsSourceProperty =
        DependencyProperty.Register(
            nameof(OperationsSource),
            typeof(ObservableList<FileOperation>),
            typeof(FileOpSnackbarContent),
            new PropertyMetadata(null, OnOperationsSourceChanged));

    public ObservableList<FileOperation> OperationsSource
    {
        get => (ObservableList<FileOperation>)GetValue(OperationsSourceProperty);
        set => SetValue(OperationsSourceProperty, value);
    }

    public static readonly DependencyProperty OverflowCountProperty =
        DependencyProperty.Register(
            nameof(OverflowCount),
            typeof(int),
            typeof(FileOpSnackbarContent),
            new PropertyMetadata(0));

    public int OverflowCount
    {
        get => (int)GetValue(OverflowCountProperty);
        private set
        {
            SetValue(OverflowCountProperty, value);
            OverflowString = string.Format(Strings.Resources.S_FILEOP_HIDDEN_ITEMS, value);
        }
    }

    public static readonly DependencyProperty OverflowStringProperty =
        DependencyProperty.Register(
            nameof(OverflowString),
            typeof(string),
            typeof(FileOpSnackbarContent),
            new PropertyMetadata(string.Empty));

    public string OverflowString
    {
        get => (string)GetValue(OverflowStringProperty);
        private set => SetValue(OverflowStringProperty, value);
    }

    public static readonly DependencyProperty IsMultiDeviceProperty =
        DependencyProperty.Register(
            nameof(IsMultiDevice),
            typeof(bool),
            typeof(FileOpSnackbarContent),
            new PropertyMetadata(false));

    public bool IsMultiDevice
    {
        get => (bool)GetValue(IsMultiDeviceProperty);
        private set => SetValue(IsMultiDeviceProperty, value);
    }

    private const int MaxVisibleDevices = 2;
    private const int MaxVisibleOpsPerDevice = 3;

    public ICollectionView? InProgressOperations { get; private set; }

    private static void OnOperationsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FileOpSnackbarContent)d;
        if (e.NewValue is ObservableList<FileOperation> source)
        {
            var cvs = new CollectionViewSource { Source = source };
            var view = cvs.View;
            view.Filter = o => o is FileOperation op && op.Status is FileOperation.OperationStatus.InProgress;

            if (view is ICollectionViewLiveShaping liveShaping)
            {
                liveShaping.LiveFilteringProperties.Add(nameof(FileOperation.Status));
                liveShaping.IsLiveFiltering = true;
            }

            control.InProgressOperations = view;
            view.CollectionChanged += (_, _) => control.RefreshLimitedView();
        }
        else
        {
            control.InProgressOperations = null;
        }

        control.RefreshLimitedView();
    }

    private void RefreshLimitedView()
    {
        if (InProgressOperations is null)
        {
            GroupsList.ItemsSource = null;
            OverflowCount = 0;
            IsMultiDevice = false;
            return;
        }

        var all = InProgressOperations.OfType<FileOperation>().ToList();

        var byDevice = all
            .GroupBy(op => op.Device.Name)
            .ToList();

        IsMultiDevice = byDevice.Count > 1;

        var visibleGroups = byDevice
            .Take(MaxVisibleDevices)
            .Select(g => new FileOpDeviceGroup(g.Key, [.. g.Take(MaxVisibleOpsPerDevice)]))
            .ToList();

        int hiddenOps = byDevice
            .Take(MaxVisibleDevices)
            .Sum(g => Math.Max(0, g.Count() - MaxVisibleOpsPerDevice))
            + byDevice.Skip(MaxVisibleDevices).Sum(g => g.Count());

        GroupsList.ItemsSource = visibleGroups;
        OverflowCount = hiddenOps;
    }

    public FileOpSnackbarContent()
    {
        InitializeComponent();
    }
}
