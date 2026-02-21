using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class DevicesViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ICollectionView _primaryDevicesView;

    [ObservableProperty]
    private ICollectionView _secondaryDevicesView;

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();
        else
            PrimaryDevicesView.Refresh();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        PrimaryDevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.PrimaryDevices);
        PrimaryDevicesView.Filter = DeviceHelper.DevicesFilter;

        SecondaryDevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.SecondaryDevices);
        SecondaryDevicesView.Filter = DeviceHelper.DevicesFilter;

        _isInitialized = true;
    }
}
