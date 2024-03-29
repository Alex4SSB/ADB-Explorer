﻿using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public class SettingsGroupTemplateSelector : DataTemplateSelector
{
    public DataTemplate SettingsGroupTemplate { get; set; }
    public DataTemplate SettingsSeparatorTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            SettingsGroup => SettingsGroupTemplate,
            SettingsSeparator => SettingsSeparatorTemplate,
            Ungrouped => null,
            _ => throw new NotImplementedException(),
        };
    }
}
