using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class ActionButtonTemplateSelector : DataTemplateSelector
{
    public DataTemplate ButtonTemplate { get; set; }
    public DataTemplate AccentButtonTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            ActionAccentButton => AccentButtonTemplate,
            ActionButton => ButtonTemplate,
            _ => throw new NotSupportedException(),
        };
    }
}
