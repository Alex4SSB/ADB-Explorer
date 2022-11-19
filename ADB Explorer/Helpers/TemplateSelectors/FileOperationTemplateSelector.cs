using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public class FileOperationTemplateSelector : DataTemplateSelector
{
    public DataTemplate PullTemplate { get; set; }
    public DataTemplate PushTemplate { get; set; }
    public DataTemplate SyncTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            FilePullOperation => PullTemplate,
            FilePushOperation => PushTemplate,
            FileSyncOperation => SyncTemplate,
            _ => throw new System.NotImplementedException(),
        };
    }
}
