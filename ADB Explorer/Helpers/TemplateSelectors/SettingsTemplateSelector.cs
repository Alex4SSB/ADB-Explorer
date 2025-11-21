using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public class SettingsTemplateSelector : DataTemplateSelector
{
    public DataTemplate BoolSettingTemplate { get; set; }
    public DataTemplate StringSettingTemplate { get; set; }
    public DataTemplate EnumSettingTemplate { get; set; }
    public DataTemplate CultureInfoSettingTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            BoolSetting => BoolSettingTemplate,
            StringSetting => StringSettingTemplate,
            EnumSetting => EnumSettingTemplate,
            ComboSetting<CultureInfo> => CultureInfoSettingTemplate,
            _ => throw new NotImplementedException(),
        };
    }
}
