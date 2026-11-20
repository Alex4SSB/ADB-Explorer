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

    /// <summary>Where the card was pressed, for a drag to open the device in File Explorer; null when it can't start one.</summary>
    private Point? _dragStart;

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The name box and the card's buttons keep their own presses
        var source = e.OriginalSource as DependencyObject;
        if (HitTestHelper.FindAncestor<TextBoxBase>(source) is not null || IsInButtonOf(source, (DependencyObject)sender))
        {
            _dragStart = null;
            return;
        }

        _dragStart = e.GetPosition((IInputElement)sender);
    }

    /// <summary>Whether the press is on a button inside the card; the card is a button itself.</summary>
    private static bool IsInButtonOf(DependencyObject? source, DependencyObject card)
    {
        for (var current = source; current is not null && current != card; current = HitTestHelper.GetVisualOrLogicalParent(current))
        {
            if (current is ButtonBase)
                return true;
        }

        return false;
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }

        if (_dragStart is not { } start || sender is not FrameworkElement card || DataContext is not LogicalDeviceViewModel device)
            return;

        if ((e.GetPosition(card) - start).LengthSquared < AdbExplorerConst.DRAG_START_DISTANCE_SQUARED)
            return;

        // The card took the mouse on the press, as a button does; let go, so it isn't left pressed
        _dragStart = null;
        card.ReleaseMouseCapture();
        DeviceMountDrag.Start(card, device);
    }

    private void NameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not LogicalDeviceViewModel device)
            return;

        device.CancelNameEdit();
        e.Handled = true;
    }
}
