namespace ADB_Explorer.Controls;

public class AdbContextMenu : ContextMenu
{
    public bool ShowInputGestures
    {
        get => (bool)GetValue(ShowInputGesturesProperty);
        set => SetValue(ShowInputGesturesProperty, value);
    }

    public static readonly DependencyProperty ShowInputGesturesProperty =
        DependencyProperty.Register(nameof(ShowInputGestures), typeof(bool),
            typeof(AdbContextMenu), new PropertyMetadata(true));

    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem or Separator;
}
