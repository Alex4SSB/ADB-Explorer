using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages;

public partial class TerminalPage : INavigableView<TerminalViewModel>
{
    public TerminalViewModel ViewModel { get; }

    public TerminalPage(TerminalViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }
}
