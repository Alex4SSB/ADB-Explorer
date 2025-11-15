using ADB_Explorer__WpfUi.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer__WpfUi.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
