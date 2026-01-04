using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class DevicesViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ICollectionView _devicesView;

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();
        else
            DevicesView.Refresh();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        DevicesView = CollectionViewSource.GetDefaultView(Data.DevicesObject.UIList);
        DevicesView.Filter = DeviceHelper.DevicesFilter;

        _isInitialized = true;
    }
}
