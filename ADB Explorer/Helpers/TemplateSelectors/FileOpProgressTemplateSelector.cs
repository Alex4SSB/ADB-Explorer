using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal class FileOpProgressTemplateSelector : DataTemplateSelector
{
    public DataTemplate WaitingOpProgressTemplate { get; set; }
    public DataTemplate InProgSyncProgressTemplate { get; set; }
    public DataTemplate InProgShellProgressTemplate { get; set; }
    public DataTemplate CompletedSyncProgressTemplate { get; set; }
    public DataTemplate CompletedShellProgressTemplate { get; set; }
    public DataTemplate CanceledOpProgressTemplate { get; set; }
    public DataTemplate FailedOpProgressTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is null)
            return null;

        return item switch
        {
            WaitingOpProgressViewModel => WaitingOpProgressTemplate,
            InProgSyncProgressViewModel => InProgSyncProgressTemplate,
            InProgShellProgressViewModel => InProgShellProgressTemplate,
            CompletedSyncProgressViewModel => CompletedSyncProgressTemplate,
            CompletedShellProgressViewModel => CompletedShellProgressTemplate,
            CanceledOpProgressViewModel => CanceledOpProgressTemplate,
            FailedOpProgressViewModel => FailedOpProgressTemplate,
            _ => throw new NotSupportedException(),
        };
    }
}
