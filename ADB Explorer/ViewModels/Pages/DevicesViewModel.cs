using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class DevicesViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    public partial ICollectionView PrimaryDevicesView { get; set; }

    [ObservableProperty]
    public partial ICollectionView SecondaryDevicesView { get; set; }

    [ObservableProperty]
    public partial ICollectionView EmulatorDevicesView { get; set; }

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();
        else
        {
            PrimaryDevicesView.Refresh();
            EmulatorDevicesView?.Refresh();
        }

        Data.CurrentPage.Value = typeof(Views.Pages.DevicesPage);

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        PrimaryDevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.PrimaryDevices);
        PrimaryDevicesView.Filter = DeviceHelper.DevicesFilter;

        EmulatorDevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.EmulatorDevices);
        EmulatorDevicesView.Filter = DeviceHelper.DevicesFilter;
        EmulatorDevicesView.SortDescriptions.Clear();
        EmulatorDevicesView.SortDescriptions.Add(new SortDescription(nameof(DeviceViewModel.Type), ListSortDirection.Ascending));

        SecondaryDevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.SecondaryDevices);
        SecondaryDevicesView.Filter = DeviceHelper.DevicesFilter;

        _isInitialized = true;
    }

    private void RefreshViews()
    {
        PrimaryDevicesView?.Refresh();
        EmulatorDevicesView?.Refresh();
        SecondaryDevicesView?.Refresh();
    }

#if DEBUG
    [RelayCommand]
    private void TestAddLocal() { DeviceHelper.TestDevices_AddLocal(); RefreshViews(); }

    [RelayCommand]
    private void TestAddRemote() { DeviceHelper.TestDevices_AddRemote(); RefreshViews(); }

    [RelayCommand]
    private void TestAddEmulator() { DeviceHelper.TestDevices_AddEmulator(); RefreshViews(); }

    [RelayCommand]
    private void TestAddRecovery() { DeviceHelper.TestDevices_AddRecovery(); RefreshViews(); }

    [RelayCommand]
    private void TestAddUnauthorized() { DeviceHelper.TestDevices_AddUnauthorized(); RefreshViews(); }

    [RelayCommand]
    private void TestAddOffline() { DeviceHelper.TestDevices_AddOffline(); RefreshViews(); }

    [RelayCommand]
    private void TestAddPairingService() { DeviceHelper.TestDevices_AddPairingService(); RefreshViews(); }

    [RelayCommand]
    private void TestAddQrService() { DeviceHelper.TestDevices_AddQrService(); RefreshViews(); }

    [RelayCommand]
    private void TestClear() { DeviceHelper.TestDevices_Clear(); RefreshViews(); }
#endif
}
