using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages
{
    public partial class ExplorerPage : INavigableView<ExplorerViewModel>
    {
        public ExplorerViewModel ViewModel { get; }

        public ExplorerPage(ExplorerViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
