namespace ADB_Explorer.Services;

/// <summary>
/// Deletes members from a tar archive via extract + remove + repack.
/// Multiple members of the same archive are deleted in one repack.
/// </summary>
public class FileArchiveDeleteOperation : AbstractShellFileOperation
{
    public string TarArchivePath { get; }
    public IReadOnlyList<FileClass> Members { get; }

    private FileArchiveDeleteOperation(
        FileClass displaySource,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher,
        string tarArchivePath,
        IReadOnlyList<FileClass> members)
        : base(displaySource, device, dispatcher)
    {
        TarArchivePath = tarArchivePath;
        Members = members;
        OperationName = OperationType.Delete;
        AltTarget = new(Navigation.SpecialLocation.devNull);
    }

    public static FileArchiveDeleteOperation Create(
        IReadOnlyList<FileClass> members,
        string tarArchivePath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
    {
        if (members.Count == 0)
            throw new ArgumentException("No members.", nameof(members));

        return new(members[0], device, dispatcher, tarArchivePath, members);
    }

    public override void Start()
    {
        BeginInProgress();

        var operationTask = Task.Run(() =>
        {
            var internalPaths = new List<string>(Members.Count);
            foreach (var member in Members)
            {
                if (!ArchivePath.TryParse(member.FullPath, out var archive, out var internalPath, Device.ID)
                    || archive != TarArchivePath
                    || string.IsNullOrEmpty(internalPath))
                {
                    throw new InvalidOperationException($"Invalid archive member: {member.FullPath}");
                }

                internalPaths.Add(internalPath);
            }

            var session = ArchiveOpProgressSession.FromToc(
                this,
                Device.ID,
                TarArchivePath,
                CancelTokenSource!.Token,
                phases: 2);

            ArchiveExtract.DeleteTarMembers(
                Device.ID,
                TarArchivePath,
                internalPaths,
                CancelTokenSource!.Token,
                session.OnLine,
                session.BeginPhase);

            session.Finish();
        }, CancelTokenSource!.Token);

        TrackTask(operationTask, "Archive delete failed");
    }
}
