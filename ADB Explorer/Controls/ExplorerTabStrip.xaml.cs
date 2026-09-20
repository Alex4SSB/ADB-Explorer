using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ExplorerTabStrip.xaml
/// </summary>
public partial class ExplorerTabStrip : UserControl
{
    public ExplorerTabStrip()
    {
        DataContext = App.Services.GetService<ExplorerTabsViewModel>();

        InitializeComponent();

        // Deferred: the shortcut text comes from AppActions, which isn't ready while the main window is still being built.
        Loaded += (_, _) => AddTabButton.ToolTip = ((ExplorerTabsViewModel)DataContext).NewTabTooltip;
    }

    private void AddTab_Click(object sender, RoutedEventArgs e) =>
        ((ExplorerTabsViewModel)DataContext).AddTab();

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExplorerInstance tab)
            return;

        ((ExplorerTabsViewModel)DataContext).CloseTab(tab);
        e.Handled = true;
    }
}
