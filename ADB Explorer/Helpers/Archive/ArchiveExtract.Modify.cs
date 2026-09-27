namespace ADB_Explorer.Helpers;

public static partial class ArchiveExtract
{
    /// <summary>
    /// Replaces a zip archive member from a file under <paramref name="contentRoot"/> whose
    /// relative path is <paramref name="internalPath"/> (<c>cd contentRoot &amp;&amp; zip -uq archive member</c>).
    /// </summary>
    public static void UpdateZipMember(
        string deviceId,
        string archivePath,
        string internalPath,
        string contentRoot,
        CancellationToken cancellationToken = default)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot update the archive root.");

        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Zip)
            throw new InvalidOperationException($"Cannot update member in non-zip archive: {archivePath}");

        if (!ArchiveHelper.CanModify(FileHelper.GetFullName(archivePath), deviceId))
            throw new InvalidOperationException($"Archive is read-only: {archivePath}");

        var zip = ShellCommands.TranslateCommand("zip");
        var archiveEsc = AdbService.EscapeAdbShellString(archivePath);
        var rootEsc = AdbService.EscapeAdbShellString(contentRoot);
        var memberEsc = AdbService.EscapeAdbShellString(internalPath);

        // Info-ZIP: update (or add) member from a file whose path relative to cwd matches the archive path.
        var script = $"cd {rootEsc} && {zip} -uq {archiveEsc} {memberEsc}";
        var exitCode = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out var stderr,
            cancellationToken,
            "-c",
            AdbService.EscapeAdbShellString(script));

        AdbService.ThrowIfFailed(exitCode, stdout, stderr);
    }

    /// <summary>
    /// Extracts the entire tar archive into <paramref name="contentRoot"/>, then recreates it
    /// from that tree (preserving compression via the temp filename extension).
    /// Incoming members must already exist under <paramref name="contentRoot"/> at their archive-relative paths
    /// before calling this, or call <see cref="UpdateTarMember"/> / overlay helpers first.
    /// </summary>
    public static void RepackTarArchive(
        string deviceId,
        string archivePath,
        string contentRoot,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        var extension = FileHelper.GetExtension(FileHelper.GetFullName(archivePath));
        if (string.IsNullOrEmpty(extension))
            extension = ".tar";

        // Keep the original extension so toybox auto-selects gzip/bzip2/xz/zstd from the name.
        var tempArchive = FileHelper.ConcatPaths(FileHelper.GetParentPath(contentRoot), $"repack-{Guid.NewGuid():N}{extension}");
        var tar = ShellCommands.TranslateCommand("tar");
        var rootEsc = AdbService.EscapeAdbShellString(contentRoot);
        var tempEsc = AdbService.EscapeAdbShellString(tempArchive);

        // Pack top-level names via -T (not ".") so members are stored as "path"
        // rather than "./path". The latter breaks later extract-by-name on toybox.
        // -1: one name per line so names with spaces survive the pipe into tar -T.
        // -v is its own token so create/extract progress can share the same streaming path.
        string script;
        if (onVerbose is not null)
            script = $"cd {rootEsc} && ls -A1 | {tar} -cf {tempEsc} -v -T -";
        else
            script = $"cd {rootEsc} && ls -A1 | {tar} -cf {tempEsc} -T -";

        ExecuteTarCreateScript(deviceId, tempArchive, script, cancellationToken, onVerbose);

        var moveExit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var moveStdout,
            out var moveStderr,
            cancellationToken,
            "-f",
            AdbService.EscapeAdbShellString(tempArchive),
            AdbService.EscapeAdbShellString(archivePath));

        if (moveExit != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveDeviceTree(deviceId, tempArchive);
            throw new IOException(AdbService.ErrorOutput(moveStdout, moveStderr));
        }
    }

    /// <summary>
    /// Full extract of <paramref name="archivePath"/> into <paramref name="contentRoot"/> (must already exist).
    /// </summary>
    public static void ExtractEntireTar(
        string deviceId,
        string archivePath,
        string contentRoot,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null)
    {
        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Tar)
            throw new InvalidOperationException($"Cannot extract non-tar archive: {archivePath}");

        var exitCode = ExtractTar(deviceId, archivePath, contentRoot, members: [], cancellationToken, out var stdout, out var stderr, onVerbose);
        if (exitCode != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = AdbService.ErrorOutput(stdout, stderr);
            throw new IOException(string.IsNullOrWhiteSpace(detail)
                ? $"Failed to extract from {archivePath}"
                : $"Failed to extract from {archivePath}: {detail.Trim()}");
        }
    }

    /// <summary>
    /// Replaces or adds a tar member: extract whole archive into <paramref name="contentRoot"/>
    /// (must already exist and be empty or only contain the incoming member), merge any
    /// pre-pushed member at <paramref name="internalPath"/>, then repack.
    /// Prefer extracting first, then writing the member, then <see cref="RepackTarArchive"/>.
    /// </summary>
    public static void UpdateTarMember(
        string deviceId,
        string archivePath,
        string internalPath,
        string contentRoot,
        CancellationToken cancellationToken = default)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot update the archive root.");

        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        // Incoming member may already sit under contentRoot; stash it, extract, restore, then pack.
        var stagingParent = FileHelper.GetParentPath(contentRoot);
        var incomingRoot = FileHelper.ConcatPaths(stagingParent, "incoming");
        var memberSource = FileHelper.ConcatPaths(contentRoot, internalPath);
        var incomingMember = FileHelper.ConcatPaths(incomingRoot, internalPath);

        ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(incomingMember)]).GetAwaiter().GetResult();

        var stashExit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var stashStdout,
            out var stashStderr,
            cancellationToken,
            AdbService.EscapeAdbShellString(memberSource),
            AdbService.EscapeAdbShellString(incomingMember));

        AdbService.ThrowIfFailed(stashExit, stashStdout, stashStderr, cancellationToken);

        // Clear leftover empty parents under contentRoot, then extract into it.
        RemoveDeviceTree(deviceId, contentRoot);
        ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();
        ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

        var memberDest = FileHelper.ConcatPaths(contentRoot, internalPath);
        ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(memberDest)]).GetAwaiter().GetResult();
        AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "rm",
            out _,
            out _,
            cancellationToken,
            "-rf",
            AdbService.EscapeAdbShellString(memberDest));

        var restoreExit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var stdout,
            out var stderr,
            cancellationToken,
            AdbService.EscapeAdbShellString(incomingMember),
            AdbService.EscapeAdbShellString(memberDest));

        AdbService.ThrowIfFailed(restoreExit, stdout, stderr, cancellationToken);

        RemoveDeviceTree(deviceId, incomingRoot);
        RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
    }

    /// <summary>
    /// Extracts the whole tar into a staging tree, lets <paramref name="edit"/> change it, then repacks it in place.
    /// </summary>
    private static void EditTarInPlace(
        string deviceId,
        string archivePath,
        Action<string> edit,
        CancellationToken cancellationToken,
        Action<string>? onVerbose,
        Action? onExtractComplete)
    {
        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            onExtractComplete?.Invoke();

            edit(contentRoot);

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Adds or replaces items inside a tar archive (device-side copy/move or Windows push overlay).
    /// <paramref name="populateOverlay"/> copies/pushes incoming files into
    /// <c>{contentRoot}/{internalDest}/</c> after the archive is extracted.
    /// </summary>
    public static void AddOrUpdateTarMembers(
        string deviceId,
        string archivePath,
        string internalDestDir,
        Action<string, CancellationToken> populateOverlay,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        internalDestDir = ArchivePath.NormalizeInternal(internalDestDir);

        EditTarInPlace(deviceId, archivePath, contentRoot =>
        {
            var overlayDest = string.IsNullOrEmpty(internalDestDir)
                ? contentRoot
                : FileHelper.ConcatPaths(contentRoot, internalDestDir);
            ShellFileOperation.MakeDirs(deviceId, [overlayDest]).GetAwaiter().GetResult();

            populateOverlay(overlayDest, cancellationToken);
        }, cancellationToken, onVerbose, onExtractComplete);
    }

    /// <summary>
    /// Removes members from a tar archive (extract entire archive, <c>rm -rf</c> each path, repack).
    /// </summary>
    public static void DeleteTarMembers(
        string deviceId,
        string archivePath,
        IReadOnlyList<string> internalPaths,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        var normalized = internalPaths
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("No archive members to delete.");

        EditTarInPlace(deviceId, archivePath, contentRoot =>
        {
            foreach (var internalPath in normalized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = FileHelper.ConcatPaths(contentRoot, internalPath);
                var exit = AdbService.ExecuteDeviceAdbShellCommand(
                    deviceId,
                    "rm",
                    out var stdout,
                    out var stderr,
                    cancellationToken,
                    "-rf",
                    AdbService.EscapeAdbShellString(target));

                if (exit != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var detail = AdbService.ErrorOutput(stdout, stderr);
                    throw new IOException(string.IsNullOrWhiteSpace(detail)
                        ? $"Failed to delete {internalPath} from {archivePath}"
                        : $"Failed to delete {internalPath} from {archivePath}: {detail.Trim()}");
                }
            }
        }, cancellationToken, onVerbose, onExtractComplete);
    }

    /// <summary>
    /// Renames a tar member (file or directory tree) via extract + <c>mv</c> + repack.
    /// </summary>
    public static void RenameTarMember(
        string deviceId,
        string archivePath,
        string oldInternalPath,
        string newInternalPath,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        oldInternalPath = ArchivePath.NormalizeInternal(oldInternalPath);
        newInternalPath = ArchivePath.NormalizeInternal(newInternalPath);
        if (string.IsNullOrEmpty(oldInternalPath) || string.IsNullOrEmpty(newInternalPath))
            throw new InvalidOperationException("Cannot rename the archive root.");

        if (oldInternalPath == newInternalPath)
            return;

        EditTarInPlace(deviceId, archivePath, contentRoot =>
        {
            var source = FileHelper.ConcatPaths(contentRoot, oldInternalPath);
            var dest = FileHelper.ConcatPaths(contentRoot, newInternalPath);
            ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(dest)]).GetAwaiter().GetResult();

            var exit = AdbService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                AdbService.EscapeAdbShellString(source),
                AdbService.EscapeAdbShellString(dest));

            AdbService.ThrowIfFailed(exit, stdout, stderr, cancellationToken);
        }, cancellationToken, onVerbose, onExtractComplete);
    }

    /// <summary>
    /// Creates an empty file or directory inside a tar archive via extract + touch/mkdir + repack.
    /// </summary>
    public static void CreateTarMember(
        string deviceId,
        string archivePath,
        string internalPath,
        bool isDirectory,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot create the archive root.");

        EditTarInPlace(deviceId, archivePath, contentRoot =>
        {
            var target = FileHelper.ConcatPaths(contentRoot, internalPath);
            if (isDirectory)
            {
                ShellFileOperation.MakeDirs(deviceId, [target]).GetAwaiter().GetResult();
            }
            else
            {
                ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(target)]).GetAwaiter().GetResult();
                var touchExit = AdbService.ExecuteDeviceAdbShellCommand(
                    deviceId,
                    "touch",
                    out var stdout,
                    out var stderr,
                    cancellationToken,
                    AdbService.EscapeAdbShellString(target));

                AdbService.ThrowIfFailed(touchExit, stdout, stderr, cancellationToken);
            }
        }, cancellationToken, onVerbose, onExtractComplete);
    }
}
