using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class FileOpFileNameTemplateSelector : DataTemplateSelector
{
    public DataTemplate UninstallOpFileNameTemplate { get; set; }
    public DataTemplate FolderCompletedOpFileNameTemplate { get; set; }
    public DataTemplate FolderInProgOpFileNameTemplate { get; set; }
    public DataTemplate RegularOpFileNameTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is not FileOperation fileop)
            return null;

        if (fileop is PackageInstallOperation pkgInstall && pkgInstall.IsUninstall)
            return UninstallOpFileNameTemplate;

        if (fileop.FilePath.IsDirectory)
            return fileop.Status is FileOperation.OperationStatus.InProgress ? FolderInProgOpFileNameTemplate : FolderCompletedOpFileNameTemplate;

        return RegularOpFileNameTemplate;
    }
}
