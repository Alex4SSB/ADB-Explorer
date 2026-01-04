using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages;

public partial class DevicesPage : INavigableView<DevicesViewModel>
{
    public DevicesViewModel ViewModel { get; }

    public DevicesPage(DevicesViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
    }

    private void UIList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FilterDevices();
    }

    private void DevicesObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Devices.UIList))
            FilterDevices();
    }

    private void FilterDevices() => DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(DevicesList.ItemsSource));
}
