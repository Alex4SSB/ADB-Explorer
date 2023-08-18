namespace ADB_Explorer.Converters;

class MenuItemConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DependencyObject parent = value as UIElement;
        while (parent is not null and not Menu)
            parent = VisualTreeHelper.GetParent(parent);

        return parameter switch
        {
            "Padding" => Helpers.MenuHelper.GetItemPadding(parent as UIElement),
            "Margin" => Helpers.MenuHelper.GetItemMargin(parent as UIElement),
            "Style" => Helpers.MenuHelper.GetIsButtonMenu(parent as UIElement),
            _ => throw new NotSupportedException(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
