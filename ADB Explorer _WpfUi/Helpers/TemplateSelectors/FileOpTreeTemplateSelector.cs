using ADB_Explorer.Models;

namespace ADB_Explorer.Helpers;

internal class FileOpTreeTemplateSelector : DataTemplateSelector
{
    public HierarchicalDataTemplate FileOpTreeFolderTemplate { get; set; }

    public HierarchicalDataTemplate FileOpTreeFileTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            SyncFile dir when dir.IsDirectory => FileOpTreeFolderTemplate,
            SyncFile => FileOpTreeFileTemplate,
            _ => throw new NotSupportedException(),
        };
    }
}
