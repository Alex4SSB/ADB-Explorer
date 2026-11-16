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

    private void Card_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // The name box keeps its own text menu
        if (HitTestHelper.FindAncestor<TextBoxBase>(e.OriginalSource as DependencyObject) is not null
            || DataContext is not LogicalDeviceViewModel device)
            return;

        DeviceContextMenu.SetFor(device);
        if (DeviceContextMenu.VisibleList.Count == 0)
            e.Handled = true;
    }

    private void NameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not LogicalDeviceViewModel device)
            return;

        device.CancelNameEdit();
        e.Handled = true;
    }
}
