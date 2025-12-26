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
    }
}
