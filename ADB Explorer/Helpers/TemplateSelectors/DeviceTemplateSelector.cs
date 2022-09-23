using ADB_Explorer.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public class DeviceTemplateSelector : DataTemplateSelector
    {
        public DataTemplate LogicalDeviceTemplate { get; set; }
        public DataTemplate ServiceDeviceTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                UILogicalDevice => LogicalDeviceTemplate,
                UIServiceDevice => ServiceDeviceTemplate,
                _ => throw new NotImplementedException(),
            };
        }
    }
}
