using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class MenuTemplateSelector : DataTemplateSelector
{
    public DataTemplate CompoundIconMenuTemplate { get; set; }
    public DataTemplate IconMenuTemplate { get; set; }
    public DataTemplate DynamicAltTextTemplate { get; set; }
    public DataTemplate SeparatorTemplate { get; set; }
    public DataTemplate SubMenuTemplate { get; set; }
    public DataTemplate SubMenuSeparatorTemplate { get; set; }
    public DataTemplate DualActionButtonTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item switch
    {
        DualActionButton => DualActionButtonTemplate,
        SubMenuSeparator => SubMenuSeparatorTemplate,
        MenuSeparator => SeparatorTemplate,
        SubMenu or string => SubMenuTemplate,
        AltTextMenu => DynamicAltTextTemplate,
        IconMenu => IconMenuTemplate,
        CompoundIconMenu => CompoundIconMenuTemplate,
        null => new(),
        _ => throw new NotSupportedException(),
    };
}

internal class MenuStyleSelector : StyleSelector
{
    public Style IconMenuStyle { get; set; }
    public Style DynamicAltTextStyle { get; set; }
    public Style SeparatorStyle { get; set; }
    public Style SubMenuStyle { get; set; }
    public Style SubMenuSeparatorStyle { get; set; }
    public Style CompoundIconMenuStyle { get; set; }
    public Style DualActionButtonStyle { get; set; }
    public Style DummySubMenuStyle { get; set; }

    public override Style SelectStyle(object item, DependencyObject container) => item switch
    {
        DualActionButton => DualActionButtonStyle,
        DummySubMenu => DummySubMenuStyle,
        SubMenuSeparator => SubMenuSeparatorStyle,
        MenuSeparator => SeparatorStyle,
        SubMenu or string => SubMenuStyle,
        AltTextMenu => DynamicAltTextStyle,
        IconMenu => IconMenuStyle,
        CompoundIconMenu => CompoundIconMenuStyle,
        _ => throw new NotSupportedException(),
    };
}
