using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

public record FileOpDeviceGroup(string DeviceName, IList<FileOperation> Operations);

public partial class FileOpSnackbarContent : UserControl
{
    public static readonly DependencyProperty OperationsSourceProperty =
        DependencyProperty.Register(
            nameof(OperationsSource),
            typeof(IReadOnlyList<FileOperation>),
            typeof(FileOpSnackbarContent),
            new PropertyMetadata(null, OnOperationsSourceChanged));

    /// <summary>
    /// The exact set of operations to display, already curated (in progress + operations still
    /// within their post-completion grace period) by <see cref="Services.FileOpSnackbarService"/>.
    /// </summary>
    public IReadOnlyList<FileOperation> OperationsSource
    {
        get => (IReadOnlyList<FileOperation>)GetValue(OperationsSourceProperty);
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

    private static void OnOperationsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FileOpSnackbarContent)d).RefreshLimitedView();

    private void RefreshLimitedView()
    {
        var all = OperationsSource;
        if (all is null or { Count: 0 })
        {
            GroupsList.ItemsSource = null;
            OverflowCount = 0;
            IsMultiDevice = false;
            return;
        }

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
