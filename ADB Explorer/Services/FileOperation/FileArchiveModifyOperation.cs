using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

/// <summary>
/// Adds or replaces members inside a tar archive via extract + repack.
/// Supports device-side paste (copy/move) and Windows push into the archive.
/// </summary>
public class FileArchiveModifyOperation : AbstractShellFileOperation
{
    public string TarArchivePath { get; }
    public string InternalDestDir { get; }
    public IReadOnlyList<FileClass> DeviceSources { get; }
    public IReadOnlyList<ShellItem> WindowsSources { get; }
    public bool IsMove { get; }

    private FileArchiveModifyOperation(
        FileClass displaySource,
        SyncFile targetPath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher,
        string tarArchivePath,
        string internalDestDir,
        IReadOnlyList<FileClass> deviceSources,
        IReadOnlyList<ShellItem> windowsSources,
        OperationType operationType,
        bool isMove)
        : base(displaySource, device, dispatcher)
    {
        TarArchivePath = tarArchivePath;
        InternalDestDir = ArchivePath.NormalizeInternal(internalDestDir);
        DeviceSources = deviceSources;
        WindowsSources = windowsSources;
        IsMove = isMove;
        TargetPath = targetPath;
        OperationName = operationType;
        AltTarget = new(ArchivePath.Join(tarArchivePath, InternalDestDir));
    }

    public static FileArchiveModifyOperation FromDevicePaste(
        IReadOnlyList<FileClass> sources,
        string archiveTargetComposite,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher,
        DragDropEffects cutType)
    {
        if (sources.Count == 0)
            throw new ArgumentException("No sources.", nameof(sources));

        if (!ArchivePath.TryParse(archiveTargetComposite, out var archivePath, out var internalDest, device.ID))
            throw new ArgumentException("Target is not an archive path.", nameof(archiveTargetComposite));

        var display = sources[0];
        var target = new SyncFile(
            ArchivePath.Join(archivePath, string.IsNullOrEmpty(internalDest)
                ? display.FullName
                : FileHelper.ConcatPaths(internalDest, display.FullName)),
            display.Type);

        return new(
            display,
            target,
            device,
            dispatcher,
            archivePath,
            internalDest,
            sources,
            [],
            cutType is DragDropEffects.Move ? OperationType.Move : OperationType.Copy,
            isMove: cutType is DragDropEffects.Move);
    }

    public static FileArchiveModifyOperation FromWindowsPush(
        IReadOnlyList<ShellItem> sources,
        string archiveTargetComposite,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
    {
        if (sources.Count == 0)
            throw new ArgumentException("No sources.", nameof(sources));

        if (!ArchivePath.TryParse(archiveTargetComposite, out var archivePath, out var internalDest, device.ID))
            throw new ArgumentException("Target is not an archive path.", nameof(archiveTargetComposite));

        var first = sources[0];
        var display = new FileClass(first);
        var target = new SyncFile(
            ArchivePath.Join(archivePath, string.IsNullOrEmpty(internalDest)
                ? first.Name
                : FileHelper.ConcatPaths(internalDest, first.Name)),
            first.IsFolder ? FileType.Folder : FileType.File);

        return new(
            display,
            target,
            device,
            dispatcher,
            archivePath,
            internalDest,
            [],
            sources,
            OperationType.Push,
            isMove: false);
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var operationTask = Task.Run(() =>
        {
            ArchiveExtract.AddOrUpdateTarMembers(
                Device.ID,
                TarArchivePath,
                InternalDestDir,
                PopulateOverlay,
                CancelTokenSource.Token);

            if (IsMove && DeviceSources.Count > 0)
                ShellFileOperation.SilentDelete(Device, DeviceSources);
        }, CancelTokenSource.Token);

        operationTask.ContinueWith(_ =>
        {
            Status = OperationStatus.Completed;
            StatusInfo = new CompletedShellProgressViewModel();
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith(_ =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith(t =>
        {
            Status = OperationStatus.Failed;
            var message = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Archive modify failed";
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(
                typeof(ShellErrorInfo),
                failed: -1,
                message: message,
                total: true));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void PopulateOverlay(string overlayDest, CancellationToken cancellationToken)
    {
        if (WindowsSources.Count > 0)
        {
            foreach (var item in WindowsSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = FileHelper.ConcatPaths(overlayDest, item.Name);

                // Clear any conflicting extracted member (file vs directory) before push.
                ADBService.ExecuteDeviceAdbShellCommand(
                    Device.ID,
                    "rm",
                    out _,
                    out _,
                    cancellationToken,
                    "-rf",
                    ADBService.EscapeAdbShellString(dest));

                ShellFileOperation.SilentPush(Device, item, dest, cancellationToken);
            }

            return;
        }

        foreach (var item in DeviceSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = FileHelper.ConcatPaths(overlayDest, item.FullName);

            ADBService.ExecuteDeviceAdbShellCommand(
                Device.ID,
                "rm",
                out _,
                out _,
                cancellationToken,
                "-rf",
                ADBService.EscapeAdbShellString(dest));

            // Always copy into the staging tree; move deletes sources only after a successful repack.
            var exit = ADBService.ExecuteDeviceAdbShellCommand(
                Device.ID,
                "cp",
                out var stdout,
                out var stderr,
                cancellationToken,
                "-a",
                ADBService.EscapeAdbShellString(item.FullPath),
                ADBService.EscapeAdbShellString(dest));

            if (exit != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }
        }
    }
}
