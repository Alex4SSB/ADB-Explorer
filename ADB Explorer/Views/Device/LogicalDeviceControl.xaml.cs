namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for LogicalDeviceControl.xaml
/// </summary>
public partial class LogicalDeviceControl : UserControl
{
    public LogicalDeviceControl()
    {
        InitializeComponent();
    }

    private void NameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } textBox)
            return;

        // The visibility change lands before the box can take focus
        Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void NameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not LogicalDeviceViewModel device)
            return;

        device.CancelNameEdit();
        e.Handled = true;
    }
}
