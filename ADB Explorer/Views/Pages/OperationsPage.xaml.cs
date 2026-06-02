using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages
{
    public partial class OperationsPage : INavigableView<OperationsViewModel>
    {
        public OperationsViewModel ViewModel { get; }

        public OperationsPage(OperationsViewModel viewModel)
        {
            Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;
            Thread.CurrentThread.CurrentUICulture = Data.Settings.ActualUICulture;

            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
