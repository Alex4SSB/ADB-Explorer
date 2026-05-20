using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class TerminalViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    public AdbTerminalDevice AdbEntry { get; } = new();

    public ObservableCollection<object> DevicesView { get; } = [];

    [ObservableProperty]
    public partial DeviceViewModel SelectedDevice { get; set; }

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        DevicesView.Add(AdbEntry);

        if (Data.DevicesObject?.UIList is not null)
        {
            foreach (var device in Data.DevicesObject.UIList.OfType<LogicalDeviceViewModel>()
                         .Where(d => d.Status is DeviceStatus.Ok))
                DevicesView.Add(device);

            foreach (var item in Data.DevicesObject.UIList)
                item.PropertyChanged += DeviceItem_PropertyChanged;

            Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        }

        SelectedDevice = DevicesView.OfType<LogicalDeviceViewModel>().FirstOrDefault();
        SelectedDevice ??= AdbEntry;

        _isInitialized = true;
    }

    private void UIList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (DeviceViewModel item in e.NewItems)
                item.PropertyChanged += DeviceItem_PropertyChanged;

        if (e.OldItems is not null)
            foreach (DeviceViewModel item in e.OldItems)
                item.PropertyChanged -= DeviceItem_PropertyChanged;

        App.SafeInvoke(RefreshDeviceList);
    }

    private void DeviceItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceViewModel.Status))
            App.SafeInvoke(RefreshDeviceList);
    }

    private void RefreshDeviceList()
    {
        var activeDevices = Data.DevicesObject.UIList
            .OfType<LogicalDeviceViewModel>()
            .Where(d => d.Status is DeviceStatus.Ok)
            .ToList();

        for (int i = DevicesView.Count - 1; i >= 0; i--)
        {
            if (DevicesView[i] is LogicalDeviceViewModel d && !activeDevices.Contains(d))
                DevicesView.RemoveAt(i);
        }

        foreach (var device in activeDevices.Where(d => !DevicesView.Contains(d)))
            DevicesView.Add(device);

        if (SelectedDevice is not LogicalDeviceViewModel { Status: DeviceStatus.Ok })
        {
            SelectedDevice = DevicesView.OfType<LogicalDeviceViewModel>().FirstOrDefault();
            SelectedDevice ??= AdbEntry;
        }
    }
}
