using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages;

public partial class DevicesPage : INavigableView<DevicesViewModel>
{
    public DevicesViewModel ViewModel { get; }

    public DevicesPage(DevicesViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}
