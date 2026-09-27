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
        BeginInProgress();

        var operationTask = Task.Run(() =>
        {
            var session = ArchiveOpProgressSession.FromToc(
                this,
                Device.ID,
                TarArchivePath,
                CancelTokenSource!.Token,
                phases: 2);

            ArchiveExtract.AddOrUpdateTarMembers(
                Device.ID,
                TarArchivePath,
                InternalDestDir,
                PopulateOverlay,
                CancelTokenSource!.Token,
                session.OnLine,
                session.BeginPhase);

            session.Finish();

            if (IsMove && DeviceSources.Count > 0)
                ShellFileOperation.SilentDelete(Device, DeviceSources);
        }, CancelTokenSource!.Token);

        TrackTask(operationTask, "Archive modify failed");
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
                AdbService.ExecuteDeviceAdbShellCommand(
                    Device.ID,
                    "rm",
                    out _,
                    out _,
                    cancellationToken,
                    "-rf",
                    AdbService.EscapeAdbShellString(dest));

                ShellFileOperation.SilentPush(Device, item, dest, cancellationToken);
            }

            return;
        }

        foreach (var item in DeviceSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = FileHelper.ConcatPaths(overlayDest, item.FullName);

            AdbService.ExecuteDeviceAdbShellCommand(
                Device.ID,
                "rm",
                out _,
                out _,
                cancellationToken,
                "-rf",
                AdbService.EscapeAdbShellString(dest));

            // Always copy into the staging tree; move deletes sources only after a successful repack.
            var exit = AdbService.ExecuteDeviceAdbShellCommand(
                Device.ID,
                "cp",
                out var stdout,
                out var stderr,
                cancellationToken,
                "-a",
                AdbService.EscapeAdbShellString(item.FullPath),
                AdbService.EscapeAdbShellString(dest));

            AdbService.ThrowIfFailed(exit, stdout, stderr, cancellationToken);
        }
    }
}
