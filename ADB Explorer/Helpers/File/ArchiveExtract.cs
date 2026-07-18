using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Extracts archive members to a real device path for paste / pull staging.
/// Files are flattened to their basename; directories keep their internal tree under the selected folder name.
/// </summary>
public static class ArchiveExtract
{
    public const string StagingFolderName = ".adb-explorer-extract";

    private static readonly ConcurrentDictionary<string, byte> ActiveStagingRoots = new(StringComparer.Ordinal);

    public static bool IsArchiveSource(FileClass file, string? deviceId = null)
        => ArchivePath.IsArchivePath(file.FullPath, deviceId);

    public static bool IsArchiveSource(IEnumerable<FileClass> files, string? deviceId = null)
        => files.Any(f => IsArchiveSource(f, deviceId));

    /// <summary>Top-level name written at the destination (basename for files and selected folders).</summary>
    public static string GetOutputName(string internalPath)
        => FileHelper.GetFullName(ArchivePath.NormalizeInternal(internalPath));

    public static string CreateStagingRoot(string deviceId, CancellationToken cancellationToken = default)
    {
        // mkdir -p on nested paths (via MakeDirs) creates this root; no need to mkdir here.
        var root = $"{AdbExplorerConst.TEMP_PATH}/{StagingFolderName}-{Guid.NewGuid():N}";
        ActiveStagingRoots.TryAdd(root, 0);
        return root;
    }

    public static void CleanupStaging(string deviceId, string stagingRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(stagingRoot)
            || !stagingRoot.StartsWith($"{AdbExplorerConst.TEMP_PATH}/{StagingFolderName}-", StringComparison.Ordinal))
            return;

        // Never cancel cleanup — a cancelled extract/pull token must not leave temp dirs behind.
        _ = cancellationToken;
        RemoveDeviceTree(deviceId, stagingRoot);
        ActiveStagingRoots.TryRemove(stagingRoot, out _);
    }

    private static void RemoveDeviceTree(string deviceId, string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "rm",
            out _,
            out _,
            CancellationToken.None,
            "-rf",
            ADBService.EscapeAdbShellString(path));
    }

    /// <summary>
    /// Deletes every staging folder under <see cref="AdbExplorerConst.TEMP_PATH"/> matching the
    /// current (and legacy) name prefix — used on app shutdown.
    /// </summary>
    public static void CleanupAllStaging(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        deviceId ??= Data.DevicesObject?.Current?.ID;
        ActiveStagingRoots.Clear();

        if (deviceId is null)
            return;

        // Glob wipe: tracked and orphaned dirs (e.g. after a crash).
        var script = $"rm -rf {AdbExplorerConst.TEMP_PATH}/{StagingFolderName}-*";

        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out _,
            out _,
            CancellationToken.None,
            "-c",
            ADBService.EscapeAdbShellString(script));
    }

    /// <summary>
    /// Fire-and-forget cleanup of currently tracked staging roots only
    /// (clipboard/drag lifecycle — avoids glob-wiping a newly created root).
    /// </summary>
    public static void BeginCleanupAllStaging(string? deviceId = null)
    {
        deviceId ??= Data.DevicesObject?.Current?.ID;

        var roots = ActiveStagingRoots.Keys.ToArray();
        foreach (var root in roots)
            ActiveStagingRoots.TryRemove(root, out _);

        if (deviceId is null || roots.Length == 0)
            return;

        var id = deviceId;
        _ = Task.Run(() =>
        {
            foreach (var root in roots)
            {
                try { RemoveDeviceTree(id, root); }
                catch { /* best-effort */ }
            }
        });
    }

    /// <summary>
    /// Extracts a single archive selection so <paramref name="destinationPath"/> becomes the file
    /// or the selected directory (with internals preserved).
    /// </summary>
    public static void ExtractSelection(
        string deviceId,
        string archivePath,
        string internalPath,
        bool isDirectory,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot extract the archive root as a selection.");

        var family = ArchiveHelper.GetFamily(archivePath);
        if (family is ArchiveFamily.None)
            throw new InvalidOperationException($"Unsupported archive: {archivePath}");

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            var destParent = FileHelper.GetParentPath(destinationPath);
            ShellFileOperation.MakeDirs(deviceId, [contentRoot, destParent]).GetAwaiter().GetResult();

            ExtractMembers(deviceId, family, archivePath, internalPath, isDirectory, contentRoot, cancellationToken);

            var extractedPath = FileHelper.ConcatPaths(contentRoot, internalPath);

            // Replace existing destination if present (caller already ran conflict UI when pasting).
            ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "rm",
                out _,
                out _,
                cancellationToken,
                "-rf",
                ADBService.EscapeAdbShellString(destinationPath));

            var moveResult = ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                ADBService.EscapeAdbShellString(extractedPath),
                ADBService.EscapeAdbShellString(destinationPath));

            if (moveResult != 0)
                throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

            RemoveDeviceTree(deviceId, contentRoot);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

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
        var archiveEsc = ADBService.EscapeAdbShellString(archivePath);
        var rootEsc = ADBService.EscapeAdbShellString(contentRoot);
        var memberEsc = ADBService.EscapeAdbShellString(internalPath);

        // Info-ZIP: update (or add) member from a file whose path relative to cwd matches the archive path.
        var script = $"cd {rootEsc} && {zip} -uq {archiveEsc} {memberEsc}";
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out var stderr,
            cancellationToken,
            "-c",
            ADBService.EscapeAdbShellString(script));

        if (exitCode != 0)
            throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
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
        CancellationToken cancellationToken = default)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        var extension = FileHelper.GetExtension(FileHelper.GetFullName(archivePath));
        if (string.IsNullOrEmpty(extension))
            extension = ".tar";

        // Keep the original extension so toybox auto-selects gzip/bzip2/xz/zstd from the name.
        var tempArchive = FileHelper.ConcatPaths(FileHelper.GetParentPath(contentRoot), $"repack-{Guid.NewGuid():N}{extension}");
        var tar = ShellCommands.TranslateCommand("tar");
        var rootEsc = ADBService.EscapeAdbShellString(contentRoot);
        var tempEsc = ADBService.EscapeAdbShellString(tempArchive);

        // Pack top-level names via -T (not ".") so members are stored as "path"
        // rather than "./path". The latter breaks later extract-by-name on toybox.
        // -1: one name per line so names with spaces survive the pipe into tar -T.
        var script = $"cd {rootEsc} && ls -A1 | {tar} -cf {tempEsc} -T -";
        var createExit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var createStdout,
            out var createStderr,
            cancellationToken,
            "-c",
            ADBService.EscapeAdbShellString(script));

        if (createExit != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveDeviceTree(deviceId, tempArchive);
            throw new IOException(string.IsNullOrWhiteSpace(createStderr) ? createStdout : createStderr);
        }

        var moveExit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var moveStdout,
            out var moveStderr,
            cancellationToken,
            "-f",
            ADBService.EscapeAdbShellString(tempArchive),
            ADBService.EscapeAdbShellString(archivePath));

        if (moveExit != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveDeviceTree(deviceId, tempArchive);
            throw new IOException(string.IsNullOrWhiteSpace(moveStderr) ? moveStdout : moveStderr);
        }
    }

    /// <summary>
    /// Full extract of <paramref name="archivePath"/> into <paramref name="contentRoot"/> (must already exist).
    /// </summary>
    public static void ExtractEntireTar(
        string deviceId,
        string archivePath,
        string contentRoot,
        CancellationToken cancellationToken = default)
    {
        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Tar)
            throw new InvalidOperationException($"Cannot extract non-tar archive: {archivePath}");

        var exitCode = ExtractTar(deviceId, archivePath, contentRoot, members: [], cancellationToken, out var stdout, out var stderr);
        if (exitCode != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
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

        var stashExit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var stashStdout,
            out var stashStderr,
            cancellationToken,
            ADBService.EscapeAdbShellString(memberSource),
            ADBService.EscapeAdbShellString(incomingMember));

        if (stashExit != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException(string.IsNullOrWhiteSpace(stashStderr) ? stashStdout : stashStderr);
        }

        // Clear leftover empty parents under contentRoot, then extract into it.
        RemoveDeviceTree(deviceId, contentRoot);
        ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();
        ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

        var memberDest = FileHelper.ConcatPaths(contentRoot, internalPath);
        ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(memberDest)]).GetAwaiter().GetResult();
        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "rm",
            out _,
            out _,
            cancellationToken,
            "-rf",
            ADBService.EscapeAdbShellString(memberDest));

        var restoreExit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "mv",
            out var stdout,
            out var stderr,
            cancellationToken,
            ADBService.EscapeAdbShellString(incomingMember),
            ADBService.EscapeAdbShellString(memberDest));

        if (restoreExit != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }

        RemoveDeviceTree(deviceId, incomingRoot);
        RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        internalDestDir = ArchivePath.NormalizeInternal(internalDestDir);

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

            var overlayDest = string.IsNullOrEmpty(internalDestDir)
                ? contentRoot
                : FileHelper.ConcatPaths(contentRoot, internalDestDir);
            ShellFileOperation.MakeDirs(deviceId, [overlayDest]).GetAwaiter().GetResult();

            populateOverlay(overlayDest, cancellationToken);

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Removes members from a tar archive (extract entire archive, <c>rm -rf</c> each path, repack).
    /// </summary>
    public static void DeleteTarMembers(
        string deviceId,
        string archivePath,
        IReadOnlyList<string> internalPaths,
        CancellationToken cancellationToken = default)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        var normalized = internalPaths
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("No archive members to delete.");

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

            foreach (var internalPath in normalized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = FileHelper.ConcatPaths(contentRoot, internalPath);
                var exit = ADBService.ExecuteDeviceAdbShellCommand(
                    deviceId,
                    "rm",
                    out var stdout,
                    out var stderr,
                    cancellationToken,
                    "-rf",
                    ADBService.EscapeAdbShellString(target));

                if (exit != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new IOException(string.IsNullOrWhiteSpace(detail)
                        ? $"Failed to delete {internalPath} from {archivePath}"
                        : $"Failed to delete {internalPath} from {archivePath}: {detail.Trim()}");
                }
            }

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Renames a tar member (file or directory tree) via extract + <c>mv</c> + repack.
    /// </summary>
    public static void RenameTarMember(
        string deviceId,
        string archivePath,
        string oldInternalPath,
        string newInternalPath,
        CancellationToken cancellationToken = default)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        oldInternalPath = ArchivePath.NormalizeInternal(oldInternalPath);
        newInternalPath = ArchivePath.NormalizeInternal(newInternalPath);
        if (string.IsNullOrEmpty(oldInternalPath) || string.IsNullOrEmpty(newInternalPath))
            throw new InvalidOperationException("Cannot rename the archive root.");

        if (oldInternalPath == newInternalPath)
            return;

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();
            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

            var source = FileHelper.ConcatPaths(contentRoot, oldInternalPath);
            var dest = FileHelper.ConcatPaths(contentRoot, newInternalPath);
            ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(dest)]).GetAwaiter().GetResult();

            var exit = ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                ADBService.EscapeAdbShellString(source),
                ADBService.EscapeAdbShellString(dest));

            if (exit != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Creates an empty file or directory inside a tar archive via extract + touch/mkdir + repack.
    /// </summary>
    public static void CreateTarMember(
        string deviceId,
        string archivePath,
        string internalPath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot create the archive root.");

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();
            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken);

            var target = FileHelper.ConcatPaths(contentRoot, internalPath);
            if (isDirectory)
            {
                ShellFileOperation.MakeDirs(deviceId, [target]).GetAwaiter().GetResult();
            }
            else
            {
                ShellFileOperation.MakeDirs(deviceId, [FileHelper.GetParentPath(target)]).GetAwaiter().GetResult();
                var touchExit = ADBService.ExecuteDeviceAdbShellCommand(
                    deviceId,
                    "touch",
                    out var stdout,
                    out var stderr,
                    cancellationToken,
                    ADBService.EscapeAdbShellString(target));

                if (touchExit != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
                }
            }

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Extracts a selection into a staging folder as <c>{stagingOutDir}/{outputName}</c>
    /// and returns that path plus a <see cref="FolderTree"/> listing for pull descriptors.
    /// Caller must <see cref="CleanupStaging"/> the returned staging root.
    /// </summary>
    public static (string StagingRoot, string ExtractedPath, FolderTree[] Tree) ExtractSelectionForPull(
        string deviceId,
        string archivePath,
        string internalPath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            throw new InvalidOperationException("Cannot extract the archive root as a selection.");

        var family = ArchiveHelper.GetFamily(archivePath);
        if (family is ArchiveFamily.None)
            throw new InvalidOperationException($"Unsupported archive: {archivePath}");

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            var outRoot = FileHelper.ConcatPaths(stagingRoot, "out");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot, outRoot]).GetAwaiter().GetResult();

            ExtractMembers(deviceId, family, archivePath, internalPath, isDirectory, contentRoot, cancellationToken);

            var extractedContent = FileHelper.ConcatPaths(contentRoot, internalPath);
            var outputName = GetOutputName(internalPath);
            var extractedPath = FileHelper.ConcatPaths(outRoot, outputName);

            var moveResult = ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                ADBService.EscapeAdbShellString(extractedContent),
                ADBService.EscapeAdbShellString(extractedPath));

            if (moveResult != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }

            // Pull reads from out/; drop the tar/unzip tree under content/ immediately.
            RemoveDeviceTree(deviceId, contentRoot);

            // Prefer listing the extracted filesystem so nested dirs are not mistaken for empty files.
            var tree = isDirectory
                ? FileHelper.GetFolderTree([extractedPath], cancellationToken: cancellationToken)
                : [];

            return (stagingRoot, extractedPath, tree);
        }
        catch
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
            throw;
        }
    }

    public static FolderTree[] GetArchiveFolderTree(
        string deviceId,
        string archivePath,
        string internalPath,
        CancellationToken cancellationToken = default)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        var toc = ArchiveListing.GetOrFetchToc(deviceId, archivePath, cancellationToken);
        return BuildFolderTreeFromEntries(internalPath, ArchivePath.Join(archivePath, internalPath), toc.Entries);
    }

    /// <summary>Entries under <paramref name="internalDirectory"/> (files and nested dirs), excluding the directory marker itself.</summary>
    public static IEnumerable<ArchiveEntry> GetDescendantEntries(IReadOnlyList<ArchiveEntry> entries, string internalDirectory)
    {
        internalDirectory = ArchivePath.NormalizeInternal(internalDirectory);
        if (string.IsNullOrEmpty(internalDirectory))
            return entries;

        var prefix = internalDirectory + "/";
        return entries.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> GetMemberPathsToExtract(
        IReadOnlyList<ArchiveEntry> entries,
        string internalPath,
        bool isDirectory)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);

        if (!isDirectory)
            return [internalPath];

        var members = new List<string>();
        if (entries.Any(e => e.Path.Equals(internalPath, StringComparison.Ordinal) && e.IsDirectory))
            members.Add(internalPath);

        members.AddRange(GetDescendantEntries(entries, internalPath).Select(e => e.IsDirectory ? e.Path + "/" : e.Path));

        // Directory with only implicit children (no dir marker in TOC)
        if (members.Count == 0)
            members.Add(internalPath + "/");

        return members;
    }

    private static void ExtractMembers(
        string deviceId,
        ArchiveFamily family,
        string archivePath,
        string internalPath,
        bool isDirectory,
        string contentRoot,
        CancellationToken cancellationToken)
    {
        var toc = ArchiveListing.GetOrFetchToc(deviceId, archivePath, cancellationToken);
        var members = GetMemberPathsToExtract(toc.Entries, internalPath, isDirectory);
        if (family is ArchiveFamily.Tar && toc.UsesDotSlashPrefix)
        {
            members = [.. members.Select(m =>
            {
                var trimmed = m.TrimEnd('/');
                return trimmed.StartsWith("./", StringComparison.Ordinal) ? trimmed : "./" + trimmed;
            })];
        }

        string stdout = "";
        string stderr = "";
        var exitCode = family switch
        {
            ArchiveFamily.Tar => ExtractTar(deviceId, archivePath, contentRoot, members, cancellationToken, out stdout, out stderr),
            ArchiveFamily.Zip => ExtractZip(deviceId, archivePath, contentRoot, members, cancellationToken, out stdout, out stderr),
            _ => -1,
        };

        if (exitCode != 0)
        {
            // ExecuteCommand returns -1 on cancel instead of throwing; don't surface that as extract failure.
            cancellationToken.ThrowIfCancellationRequested();
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new IOException(string.IsNullOrWhiteSpace(detail)
                ? $"Failed to extract from {archivePath}"
                : $"Failed to extract from {archivePath}: {detail.Trim()}");
        }
    }

    private static int ExtractTar(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken,
        out string stdout,
        out string stderr)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        // -o / --no-same-owner: skip restoring uid/gid. Rooted adb otherwise tries
        // chown (e.g. 0:0) and fails with "Operation not permitted" on Android.
        var args = new List<string>
        {
            "-xof",
            ADBService.EscapeAdbShellString(archivePath),
            "-C",
            ADBService.EscapeAdbShellString(contentRoot),
        };
        foreach (var member in members)
            args.Add(ADBService.EscapeAdbShellString(member.TrimEnd('/')));

        return ADBService.ExecuteDeviceAdbShellCommand(deviceId, tar, out stdout, out stderr, cancellationToken, [.. args]);
    }

    private static int ExtractZip(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken,
        out string stdout,
        out string stderr)
    {
        var unzip = ShellCommands.TranslateCommand("unzip");
        var args = new List<string>
        {
            "-o",
            "-q",
            ADBService.EscapeAdbShellString(archivePath),
            "-d",
            ADBService.EscapeAdbShellString(contentRoot),
        };
        args.AddRange(members.Select(m => ADBService.EscapeAdbShellString(m)));

        return ADBService.ExecuteDeviceAdbShellCommand(deviceId, unzip, out stdout, out stderr, cancellationToken, [.. args]);
    }

    /// <summary>
    /// Maps TOC descendants of <paramref name="internalPath"/> onto absolute paths under <paramref name="extractedRoot"/>.
    /// Intermediate directories are included so nested folders are not mistaken for empty files.
    /// </summary>
    public static FolderTree[] BuildFolderTreeFromEntries(
        string internalPath,
        string extractedRoot,
        IReadOnlyList<ArchiveEntry> entries)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        var prefix = string.IsNullOrEmpty(internalPath) ? "" : internalPath + "/";
        var result = new Dictionary<string, FolderTree>(StringComparer.Ordinal);

        foreach (var entry in GetDescendantEntries(entries, internalPath))
        {
            string? relative;
            if (string.IsNullOrEmpty(prefix))
                relative = entry.Path;
            else if (entry.Path.StartsWith(prefix, StringComparison.Ordinal))
                relative = entry.Path[prefix.Length..];
            else
                relative = null;

            if (string.IsNullOrEmpty(relative))
                continue;

            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var accumulated = "";
            for (var i = 0; i < segments.Length; i++)
            {
                accumulated = i == 0 ? segments[0] : accumulated + "/" + segments[i];
                var absolute = FileHelper.ConcatPaths(extractedRoot, accumulated);
                var isLast = i == segments.Length - 1;
                var isFolder = !isLast || entry.IsDirectory;

                if (isFolder)
                    result.TryAdd(absolute, new FolderTree(absolute, null, entry.Modified.ToUnixTime()));
                else
                    result[absolute] = new FolderTree(absolute, entry.Size, entry.Modified.ToUnixTime());
            }
        }

        return [.. result.Values];
    }
}
