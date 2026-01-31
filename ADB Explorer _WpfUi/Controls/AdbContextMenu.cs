namespace ADB_Explorer.Controls;

public class AdbContextMenu : ContextMenu
{
    protected override DependencyObject GetContainerForItemOverride()
        => new Wpf.Ui.Controls.MenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is Wpf.Ui.Controls.MenuItem;
}
