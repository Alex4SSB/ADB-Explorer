namespace ADB_Explorer.Helpers;

/// <summary>Picks a cell template by what the row lists - a package or a file - since their view-models share no members.</summary>
internal class BrowserItemTemplateSelector : DataTemplateSelector
{
    // Set via XAML resource declarations, not a constructor — genuinely absent until then.
    public DataTemplate? FileTemplate { get; set; }
    public DataTemplate? PackageTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is Package ? PackageTemplate : FileTemplate;
}
