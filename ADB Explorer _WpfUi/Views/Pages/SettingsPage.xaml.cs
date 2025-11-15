using ADB_Explorer__WpfUi.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer__WpfUi.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
