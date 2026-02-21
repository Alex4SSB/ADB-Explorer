namespace ADB_Explorer.Controls;

public class AdbContextMenu : ContextMenu
{
    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem;
}
