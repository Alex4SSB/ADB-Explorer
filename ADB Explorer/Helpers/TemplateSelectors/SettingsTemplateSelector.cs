using ADB_Explorer.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public class SettingsTemplateSelector : DataTemplateSelector
    {
        public DataTemplate BoolSettingTemplate { get; set; }
        public DataTemplate StringSettingTemplate { get; set; }
        public DataTemplate EnumSettingTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                BoolSetting => BoolSettingTemplate,
                StringSetting => StringSettingTemplate,
                EnumSetting => EnumSettingTemplate,
                _ => throw new NotImplementedException(),
            };
        }
    }
}
