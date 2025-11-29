namespace ADB_Explorer.Helpers;

public static class ContextMenuHelper
{
    public static readonly DependencyProperty EnableAutoCloseProperty =
        DependencyProperty.RegisterAttached(
            "EnableAutoClose",
            typeof(bool),
            typeof(ContextMenuHelper),
            new PropertyMetadata(false, OnEnableAutoCloseChanged));

    public static void SetEnableAutoClose(DependencyObject element, bool value)
        => element.SetValue(EnableAutoCloseProperty, value);

    public static bool GetEnableAutoClose(DependencyObject element)
        => (bool)element.GetValue(EnableAutoCloseProperty);

    private static void OnEnableAutoCloseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            if ((bool)e.NewValue)
                element.ContextMenuOpening += OnContextMenuOpening;
            else
                element.ContextMenuOpening -= OnContextMenuOpening;
        }
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is ContextMenu menu)
        {
            // Determine the target for CanExecute (focused element or owner)
            IInputElement target = menu.PlacementTarget ?? element;

            bool anyVisibleAndEnabled = false;

            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Command is not null)
                {
                    var command = item.Command;
                    var parameter = item.CommandParameter;

                    if (command is RoutedCommand routed)
                    {
                        var commandTarget = item.CommandTarget ?? target;
                        if (routed.CanExecute(parameter, commandTarget))
                        {
                            anyVisibleAndEnabled = true;
                            break;
                        }
                    }
                    else
                    {
                        if (command.CanExecute(parameter))
                        {
                            anyVisibleAndEnabled = true;
                            break;
                        }
                    }
                }
                else if (item.IsEnabled)
                {
                    anyVisibleAndEnabled = true;
                    break;
                }
            }


            if (!anyVisibleAndEnabled)
            {
                e.Handled = true;
            }
        }
    }
}
