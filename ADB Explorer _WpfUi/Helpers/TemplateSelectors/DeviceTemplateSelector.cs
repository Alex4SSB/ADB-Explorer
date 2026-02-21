using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

public class DeviceTemplateSelector : DataTemplateSelector
{
    public DataTemplate LogicalDeviceTemplate { get; set; }
    public DataTemplate ServiceDeviceTemplate { get; set; }
    public DataTemplate NewDeviceTemplate { get; set; }
    public DataTemplate HistoryDeviceTemplate { get; set; }
    public DataTemplate WsaPkgDeviceTemplate { get; set; }
    public DataTemplate MdnsDeviceTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item switch
    {
        LogicalDeviceViewModel => LogicalDeviceTemplate,
        ServiceDeviceViewModel => ServiceDeviceTemplate,
        HistoryDeviceViewModel => HistoryDeviceTemplate,
        NewDeviceViewModel => NewDeviceTemplate,
        WsaPkgDeviceViewModel => WsaPkgDeviceTemplate,
        MdnsDeviceViewModel => MdnsDeviceTemplate,
        _ => throw new NotImplementedException(),
    };
}
