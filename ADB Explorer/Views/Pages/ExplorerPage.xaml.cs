using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPage : INavigableView<ExplorerViewModel>
{
    public ExplorerViewModel ViewModel { get; }

    public ExplorerPage(ExplorerViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public void ShowContent(ExplorerPageContent content) => ContentHost.Content = content;
}
