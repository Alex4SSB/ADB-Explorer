namespace ADB_Explorer.Helpers;

public class DetailsTemplateSelector : DataTemplateSelector
{
    // Set via XAML resource declarations, not a constructor — genuinely absent until then.
    public DataTemplate? DetailsTemplate { get; set; }
    public DataTemplate? FolderSizeTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container) => (item switch
    {
        FolderSizeDetailsViewModel => FolderSizeTemplate,
        _ => DetailsTemplate,
    }) ?? new();
}
