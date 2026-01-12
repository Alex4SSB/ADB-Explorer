using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public class SettingsTemplateSelector : DataTemplateSelector
{
    public DataTemplate BoolSettingTemplate { get; set; }
    public DataTemplate TextboxSettingTemplate { get; set; }
    public DataTemplate EnumSettingTemplate { get; set; }
    public DataTemplate CultureInfoSettingTemplate { get; set; }
    public DataTemplate InfoSettingTemplate { get; set; }
    public DataTemplate LinkSettingTemplate { get; set; }
    public DataTemplate MultiLinkSettingTemplate { get; set; }
    public DataTemplate LongDescriptionTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            InfoSetting => InfoSettingTemplate,
            LinkSetting => LinkSettingTemplate,
            MultiLinkSetting => MultiLinkSettingTemplate,
            LongDescriptionSetting => LongDescriptionTemplate,
            BoolSetting => BoolSettingTemplate,
            TextboxSetting => TextboxSettingTemplate,
            EnumSetting => EnumSettingTemplate,
            ComboSetting<CultureInfo> => CultureInfoSettingTemplate,
            _ => throw new NotImplementedException(),
        };
    }
}
