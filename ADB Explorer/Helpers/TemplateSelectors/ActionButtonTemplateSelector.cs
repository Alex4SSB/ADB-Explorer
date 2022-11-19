using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public class ActionButtonTemplateSelector : DataTemplateSelector
{
    public DataTemplate ResetSettingTemplate { get; set; }
    public DataTemplate AnimationTipSettingTemplate { get; set; }
    public DataTemplate ClearTextSettingTemplate { get; set; }
    public DataTemplate ChangeSettingTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            ResetCommand => ResetSettingTemplate,
            ShowAnimationTipCommand => AnimationTipSettingTemplate,
            ClearTextSettingCommand => ClearTextSettingTemplate,
            ChangeDefaultPathCommand or ChangeAdbPathCommand => ChangeSettingTemplate,
            _ => throw new NotImplementedException(),
        };
    }
}
