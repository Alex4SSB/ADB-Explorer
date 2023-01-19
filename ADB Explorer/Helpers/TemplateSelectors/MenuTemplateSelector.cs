using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class MenuTemplateSelector : DataTemplateSelector
{
    public DataTemplate IconMenuTemplate { get; set; }
    public DataTemplate AnimatedNotifyTemplate { get; set; }
    public DataTemplate DynamicAltTextTemplate { get; set; }
    public DataTemplate SeparatorTemplate { get; set; }
    public DataTemplate AltIconTemplate { get; set; }
    public DataTemplate SubMenuTemplate { get; set; }
    public DataTemplate SubMenuSeparatorTemplate { get; set; }
    public DataTemplate AltObjectTemplate { get; set; }
    public DataTemplate AltIconSubMenuTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            SubMenuSeparator => SubMenuSeparatorTemplate,
            AltIconSubMenu => AltIconSubMenuTemplate,
            MenuSeparator => SeparatorTemplate,
            AnimatedNotifyMenu => AnimatedNotifyTemplate,
            SubMenu or string => SubMenuTemplate,
            AltTextMenu => DynamicAltTextTemplate,
            AltIconMenu => AltIconTemplate,
            IconMenu => IconMenuTemplate,
            AltObjectMenu => AltObjectTemplate,
            _ => throw new NotSupportedException(),
        };
    }
}

internal class MenuStyleSelector : StyleSelector
{
    public Style IconMenuStyle { get; set; }
    public Style AnimatedNotifyStyle { get; set; }
    public Style DynamicAltTextStyle { get; set; }
    public Style SeparatorStyle { get; set; }
    public Style AltIconStyle { get; set; }
    public Style SubMenuStyle { get; set; }
    public Style SubMenuSeparatorStyle { get; set; }
    public Style AltIconSubMenuStyle { get; set; }
    public Style AltObjectStyle { get; set; }

    public override Style SelectStyle(object item, DependencyObject container)
    {
        return item switch
        {
            SubMenuSeparator => SubMenuSeparatorStyle,
            AltIconSubMenu => AltIconSubMenuStyle,
            MenuSeparator => SeparatorStyle,
            AnimatedNotifyMenu => AnimatedNotifyStyle,
            SubMenu or string => SubMenuStyle,
            AltTextMenu => DynamicAltTextStyle,
            AltIconMenu => AltIconStyle,
            IconMenu => IconMenuStyle,
            AltObjectMenu => AltObjectStyle,
            _ => throw new NotSupportedException(),
        };
    }
}
