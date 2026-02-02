using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

public class DriveTemplateSelector : DataTemplateSelector
{
    public DataTemplate LogicalDriveTemplate { get; set; }
    public DataTemplate VirtualDriveTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item switch
    {
        LogicalDriveViewModel => LogicalDriveTemplate,
        VirtualDriveViewModel => VirtualDriveTemplate,
        _ => throw new NotImplementedException(),
    };
}
