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
            FileSyncOperation op when op.OperationName is FileOperation.OperationType.Pull => PullTemplate,
            FileSyncOperation op when op.OperationName is FileOperation.OperationType.Push => PushTemplate,
            FileSyncOperation => SyncTemplate,
            _ => throw new NotImplementedException(),
        };
    }
}
