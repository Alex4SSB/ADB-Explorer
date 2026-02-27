using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages
{
    public partial class TerminalPage : INavigableView<TerminalViewModel>
    {
        public TerminalViewModel ViewModel { get; }

        public TerminalPage(TerminalViewModel viewModel)
        {
            // Terminal page is not localized
            Thread.CurrentThread.CurrentCulture =
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");

            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
