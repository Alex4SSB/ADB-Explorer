using ADB_Explorer.Models;

namespace ADB_Explorer.Helpers;

public class DeviceTemplateSelector : DataTemplateSelector
{
    public DataTemplate LogicalDeviceTemplate { get; set; }
    public DataTemplate ServiceDeviceTemplate { get; set; }
    public DataTemplate NewDeviceTemplate { get; set; }
    public DataTemplate HistoryDeviceTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            UILogicalDevice => LogicalDeviceTemplate,
            UIServiceDevice => ServiceDeviceTemplate,
            UINewDevice => NewDeviceTemplate,
            UIHistoryDevice => HistoryDeviceTemplate,
            _ => throw new NotImplementedException(),
        };
    }
}
