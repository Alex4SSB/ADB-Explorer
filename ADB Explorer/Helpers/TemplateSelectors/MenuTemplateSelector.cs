using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class MenuTemplateSelector : DataTemplateSelector
{
    public DataTemplate CompoundIconMenuTemplate { get; set; }
    public DataTemplate IconMenuTemplate { get; set; }
    public DataTemplate AnimatedNotifyTemplate { get; set; }
    public DataTemplate DynamicAltTextTemplate { get; set; }
    public DataTemplate SeparatorTemplate { get; set; }
    public DataTemplate AltIconTemplate { get; set; }
    public DataTemplate SubMenuTemplate { get; set; }
    public DataTemplate SubMenuSeparatorTemplate { get; set; }
    public DataTemplate AltObjectTemplate { get; set; }
    public DataTemplate CompoundIconSubMenuTemplate { get; set; }
    public DataTemplate DualActionButtonTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item switch
    {
        DualActionButton => DualActionButtonTemplate,
        SubMenuSeparator => SubMenuSeparatorTemplate,
        CompoundIconSubMenu => CompoundIconSubMenuTemplate,
        MenuSeparator => SeparatorTemplate,
        AnimatedNotifyMenu => AnimatedNotifyTemplate,
        SubMenu or string => SubMenuTemplate,
        AltTextMenu => DynamicAltTextTemplate,
        IconMenu => IconMenuTemplate,
        AltObjectMenu => AltObjectTemplate,
        CompoundIconMenu => CompoundIconMenuTemplate,
        _ => throw new NotSupportedException(),
    };
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
    public Style CompoundIconSubMenuStyle { get; set; }
    public Style AltObjectStyle { get; set; }
    public Style CompoundIconMenuStyle { get; set; }
    public Style DualActionButtonStyle { get; set; }

    public override Style SelectStyle(object item, DependencyObject container) => item switch
    {
        DualActionButton => DualActionButtonStyle,
        SubMenuSeparator => SubMenuSeparatorStyle,
        CompoundIconSubMenu => CompoundIconSubMenuStyle,
        MenuSeparator => SeparatorStyle,
        AnimatedNotifyMenu => AnimatedNotifyStyle,
        SubMenu or string => SubMenuStyle,
        AltTextMenu => DynamicAltTextStyle,
        IconMenu => IconMenuStyle,
        AltObjectMenu => AltObjectStyle,
        CompoundIconMenu => CompoundIconMenuStyle,
        _ => throw new NotSupportedException(),
    };
}
