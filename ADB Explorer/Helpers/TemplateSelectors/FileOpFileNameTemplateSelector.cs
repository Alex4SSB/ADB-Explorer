using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class FileOpFileNameTemplateSelector : DataTemplateSelector
{
    // Set via XAML resource declarations, not a constructor — genuinely absent until then.
    public DataTemplate? UninstallOpFileNameTemplate { get; set; }
    public DataTemplate? FolderCompletedOpFileNameTemplate { get; set; }
    public DataTemplate? FolderInProgOpFileNameTemplate { get; set; }
    public DataTemplate? RegularOpFileNameTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is not FileOperation fileop)
            return null;

        if (fileop is PackageInstallOperation pkgInstall && pkgInstall.IsUninstall)
            return UninstallOpFileNameTemplate ?? new();

        if (fileop.FilePath.IsDirectory)
            return (fileop.Status is FileOperation.OperationStatus.InProgress ? FolderInProgOpFileNameTemplate : FolderCompletedOpFileNameTemplate) ?? new();

        return RegularOpFileNameTemplate ?? new();
    }
}
