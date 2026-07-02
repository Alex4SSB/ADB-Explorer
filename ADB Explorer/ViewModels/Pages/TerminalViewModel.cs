using ADB_Explorer.Models;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class TerminalViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;
    private bool _devicesAttached;

    public AdbTerminalDevice AdbEntry { get; } = new();

    public TerminalViewModel()
    {
        Data.DevicesObjectCreated += (_, _) => App.SafeInvoke(AttachToDevicesObject);
    }

    public ObservableCollection<object> DevicesView { get; } = [];

    [ObservableProperty]
    public partial DeviceViewModel SelectedDevice { get; set; }

    public Task OnNavigatedToAsync()
    {
        InitializeViewModel();

        Data.CurrentPage.Value = typeof(Views.Pages.TerminalPage);

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        if (!_isInitialized)
        {
            DevicesView.Add(AdbEntry);
            _isInitialized = true;
        }

        AttachToDevicesObject();

        SelectedDevice = DevicesView.OfType<LogicalDeviceViewModel>().FirstOrDefault();
        SelectedDevice ??= AdbEntry;
    }

    private void AttachToDevicesObject()
    {
        if (_devicesAttached || Data.DevicesObject?.UIList is null)
            return;

        _devicesAttached = true;

        foreach (var device in Data.DevicesObject.UIList.OfType<LogicalDeviceViewModel>()
                     .Where(d => d.Status is DeviceStatus.Ok))
            DevicesView.Add(device);

        foreach (var item in Data.DevicesObject.UIList)
            item.PropertyChanged += DeviceItem_PropertyChanged;

        Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;

        RefreshDeviceList();
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
        if (Data.DevicesObject?.UIList is null)
            return;

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
