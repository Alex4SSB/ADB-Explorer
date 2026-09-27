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
        BeginInProgress();

        var operationTask = CreateArchiveAsync();

        TrackTask(operationTask, "Backup failed", result =>
        {
            if (result == "")
            {
                SetCompleted();
                Dispatcher.Invoke(EnqueuePull);
                return;
            }

            CleanupTempArchive();
            SetArchiveResult(result);
        }, onAborted: CleanupTempArchive);
    }

    private async Task<string> CreateArchiveAsync()
    {
        AppBackupSources sources;
        try
        {
            sources = await Task.Run(
                () => AppBackupHelper.CollectSources(Device.ID, Package, CancelTokenSource!.Token),
                CancelTokenSource!.Token).ConfigureAwait(false);
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
            CancelTokenSource!.Token,
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
        pull.WhenFinished(_ => CleanupTempArchive());
        Data.FileOpQ.AddOperation(pull);
    }

    private void CleanupTempArchive()
    {
        ShellFileOperation.SilentDelete(Device, TempArchivePath);
        ArchiveListing.InvalidateToc(TempArchivePath);
    }
}
