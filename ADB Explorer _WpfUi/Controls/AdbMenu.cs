namespace ADB_Explorer.Controls;

public class AdbMenu : Menu
{
    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem;
}

public class AdbMenuItem : Wpf.Ui.Controls.MenuItem
{
    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem;
}
