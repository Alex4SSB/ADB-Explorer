using ADB_Explorer.Controls;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

/// <summary>
/// Creates a gzip tar of a package's APKs (and OBB if present) under <c>/data/local/tmp</c>.
/// On success, enqueues a pull of that archive to a Windows <c>.apkbkp</c> file.
/// </summary>
public class AppBackupOperation : AbstractShellFileOperation
{
    public string TempArchivePath { get; }
    public string WindowsDestPath { get; }
    public Package Package { get; }

    public override string Tooltip => Strings.Resources.S_MENU_BACKUP_PACKAGE;

    public override FrameworkElement OpIcon => CreateOpIcon(new ZipIcon());

    public AppBackupOperation(
        FileClass displayFile,
        string tempArchivePath,
        string windowsDestPath,
        Package package,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(displayFile, device, dispatcher)
    {
        TempArchivePath = tempArchivePath;
        WindowsDestPath = windowsDestPath;
        Package = package;
        TargetPath = new SyncFile(windowsDestPath)
        {
            PathType = AbstractFile.FilePathType.Windows
        };

        OperationName = OperationType.Compress;
        AltSource = new(Navigation.SpecialLocation.PackageDrive);
        AltTarget = new(FileHelper.GetParentPath(windowsDestPath));
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var operationTask = CreateArchiveAsync();

        operationTask.ContinueWith(t =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();
                Dispatcher.Invoke(EnqueuePull);
                return;
            }

            CleanupTempArchive();

            if (CancelTokenSource?.IsCancellationRequested == true || t.Result == "Canceled")
            {
                Status = OperationStatus.Canceled;
                StatusInfo = new CanceledOpProgressViewModel();
                return;
            }

            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(
                typeof(ShellErrorInfo),
                failed: -1,
                message: t.Result,
                total: true));
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith(_ =>
        {
            CleanupTempArchive();
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith(t =>
        {
            CleanupTempArchive();
            Status = OperationStatus.Failed;
            var message = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Backup failed";
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(
                typeof(ShellErrorInfo),
                failed: -1,
                message: message,
                total: true));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task<string> CreateArchiveAsync()
    {
        AppBackupSources sources;
        try
        {
            sources = await Task.Run(
                () => AppBackupHelper.CollectSources(Device.ID, Package, CancelTokenSource.Token),
                CancelTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return "Canceled";
        }
        catch (Exception e)
        {
            return e.Message;
        }

        var session = new ArchiveOpProgressSession(this, sources.MemberBytes);
        var result = await ArchiveExtract.CreateApkBackupArchiveAsync(
            Device.ID,
            TempArchivePath,
            sources.ApkParent,
            sources.ApkFileNames,
            sources.ObbPackageName,
            CancelTokenSource.Token,
            session.OnLine).ConfigureAwait(false);

        if (result == "")
            session.Finish();

        return result;
    }

    private void EnqueuePull()
    {
        var source = new SyncFile(TempArchivePath);
        var target = new SyncFile(WindowsDestPath)
        {
            PathType = AbstractFile.FilePathType.Windows
        };

        var pull = FileSyncOperation.PullFile(source, target, Device, Dispatcher);
        pull.OriginalShellItem = null;
        pull.PropertyChanged += Pull_PropertyChanged;
        Data.FileOpQ.AddOperation(pull);
    }

    private void Pull_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileSyncOperation op)
            return;

        if (e.PropertyName is not nameof(Status))
            return;

        if (op.Status is OperationStatus.Completed or OperationStatus.Failed or OperationStatus.Canceled)
        {
            CleanupTempArchive();
            op.PropertyChanged -= Pull_PropertyChanged;
        }
    }

    private void CleanupTempArchive()
    {
        ShellFileOperation.SilentDelete(Device, TempArchivePath);
        ArchiveListing.InvalidateToc(TempArchivePath);
    }
}
