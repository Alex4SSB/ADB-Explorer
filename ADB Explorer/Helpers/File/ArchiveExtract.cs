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
#if DEBUG
        ApkIconService.MarkLoadStep($"CleanupStaging: {stagingRoot}");
#endif
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
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null)
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

            ExtractMembers(deviceId, family, archivePath, internalPath, isDirectory, contentRoot, cancellationToken, onVerbose);

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
        var rootEsc = ADBService.EscapeAdbShellString(contentRoot);
        var tempEsc = ADBService.EscapeAdbShellString(tempArchive);

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
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null)
    {
        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Tar)
            throw new InvalidOperationException($"Cannot extract non-tar archive: {archivePath}");

        var exitCode = ExtractTar(deviceId, archivePath, contentRoot, members: [], cancellationToken, out var stdout, out var stderr, onVerbose);
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
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
    {
        ArchiveHelper.EnsureModifiableTar(archivePath, deviceId);

        internalDestDir = ArchivePath.NormalizeInternal(internalDestDir);

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            onExtractComplete?.Invoke();

            var overlayDest = string.IsNullOrEmpty(internalDestDir)
                ? contentRoot
                : FileHelper.ConcatPaths(contentRoot, internalDestDir);
            ShellFileOperation.MakeDirs(deviceId, [overlayDest]).GetAwaiter().GetResult();

            populateOverlay(overlayDest, cancellationToken);

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
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

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            onExtractComplete?.Invoke();

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

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
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

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();
            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            onExtractComplete?.Invoke();

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

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
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
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null,
        Action? onExtractComplete = null)
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
            ExtractEntireTar(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
            onExtractComplete?.Invoke();

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

            RepackTarArchive(deviceId, archivePath, contentRoot, cancellationToken, onVerbose);
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
        CancellationToken cancellationToken,
        Action<string>? onVerbose = null)
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
            ArchiveFamily.Tar => ExtractTar(deviceId, archivePath, contentRoot, members, cancellationToken, out stdout, out stderr, onVerbose),
            ArchiveFamily.Zip => ExtractZip(deviceId, archivePath, contentRoot, members, cancellationToken, out stdout, out stderr, onVerbose),
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
        out string stderr,
        Action<string>? onVerbose = null)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        // -o / --no-same-owner: skip restoring uid/gid. Rooted adb otherwise tries
        // chown (e.g. 0:0) and fails with "Operation not permitted" on Android.
        var flags = onVerbose is not null ? "-xvof" : "-xof";
        var args = new List<string>
        {
            flags,
            ADBService.EscapeAdbShellString(archivePath),
            "-C",
            ADBService.EscapeAdbShellString(contentRoot),
        };
        foreach (var member in members)
            args.Add(ADBService.EscapeAdbShellString(member.TrimEnd('/')));

        if (onVerbose is not null)
            return RunStreamingExtract(deviceId, tar, args, onVerbose, cancellationToken, out stdout, out stderr);

        return ADBService.ExecuteDeviceAdbShellCommand(deviceId, tar, out stdout, out stderr, cancellationToken, [.. args]);
    }

    /// <summary>
    /// Extracts specific zip members into a staging content root (paths preserved under that root).
    /// Caller must <see cref="CleanupStaging"/> the returned staging root.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ExtractSelectionForPull"/>, this does not <c>mv</c> members to a flat
    /// <c>out/</c> basename — pull from <c>contentRoot/member</c> directly.
    /// </remarks>
    public static (string StagingRoot, string ContentRoot) ExtractZipMembersToStaging(
        string deviceId,
        string archivePath,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken = default,
        bool allowMissingMembers = false)
    {
        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Zip)
            throw new InvalidOperationException($"Not a zip archive: {archivePath}");

        if (members is null || members.Count == 0)
            throw new ArgumentException("At least one member is required.", nameof(members));

#if DEBUG
        ApkIconService.MarkLoadStep(
            $"ExtractZipMembersToStaging start ({members.Count}): {string.Join(',', members)}");
        var sw = Stopwatch.StartNew();
#endif

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            ExtractZipMembersInto(
                deviceId, archivePath, contentRoot, members, cancellationToken, allowMissingMembers);

#if DEBUG
            ApkIconService.MarkLoadStep(
                $"ExtractZipMembersToStaging done ({sw.ElapsedMilliseconds}ms)");
#endif
            return (stagingRoot, contentRoot);
        }
        catch
        {
#if DEBUG
            ApkIconService.MarkLoadStep(
                $"ExtractZipMembersToStaging failed ({sw.ElapsedMilliseconds}ms)");
#endif
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Extracts named zip members into an existing content root (paths preserved).
    /// When <paramref name="allowMissingMembers"/> is true, a non-zero unzip exit is tolerated
    /// (Android/Info-ZIP still extract matches; absent names do not roll back prior files).
    /// </summary>
    public static void ExtractZipMembersInto(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken = default,
        bool allowMissingMembers = false)
    {
        if (ArchiveHelper.GetFamily(archivePath) is not ArchiveFamily.Zip)
            throw new InvalidOperationException($"Not a zip archive: {archivePath}");

        if (string.IsNullOrEmpty(contentRoot))
            throw new ArgumentException("Content root is required.", nameof(contentRoot));

        if (members is null || members.Count == 0)
            throw new ArgumentException("At least one member is required.", nameof(members));

#if DEBUG
        ApkIconService.MarkLoadStep(
            $"ExtractZipMembersInto ({members.Count}, allowMissing={allowMissingMembers}): {string.Join(',', members)}");
#endif

        var exitCode = ExtractZip(deviceId, archivePath, contentRoot, members, cancellationToken, out var stdout, out var stderr);
        if (exitCode == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        if (allowMissingMembers)
        {
#if DEBUG
            ApkIconService.MarkLoadStep(
                $"ExtractZipMembersInto non-zero exit={exitCode} (tolerated); stderr={stderr}");
#endif
            return;
        }

        throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private static int ExtractZip(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken,
        out string stdout,
        out string stderr,
        Action<string>? onVerbose = null)
    {
        var unzip = ShellCommands.TranslateCommand("unzip");
        var args = new List<string>
        {
            "-o",
        };
        if (onVerbose is null)
            args.Add("-q");

        args.Add(ADBService.EscapeAdbShellString(archivePath));
        args.Add("-d");
        args.Add(ADBService.EscapeAdbShellString(contentRoot));
        args.AddRange(members.Select(m => ADBService.EscapeAdbShellString(m)));

#if DEBUG
        var quiet = onVerbose is null ? " -q" : "";
        ApkIconService.MarkLoadStep($"ExtractZip (unzip -o{quiet}) {members.Count} member(s)");
#endif
        if (onVerbose is not null)
            return RunStreamingExtract(deviceId, unzip, args, onVerbose, cancellationToken, out stdout, out stderr);

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

    /// <summary>
    /// Builds the <c>sh -c</c> script used to create a tar-family archive at
    /// <paramref name="archivePath"/>. Compression is selected by toybox from the filename
    /// extension (e.g. <c>.tar.gz</c>). When <paramref name="sourceFullPaths"/> is empty,
    /// creates an empty archive. All sources must share the same parent directory.
    /// </summary>
    public static string BuildCreateTarArchiveScript(
        string archivePath,
        IReadOnlyList<string> sourceFullPaths,
        bool verbose = false)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        var archiveEsc = ADBService.EscapeAdbShellString(archivePath);
        var verboseFlag = verbose ? " -v" : "";

        if (sourceFullPaths.Count == 0)
            return $"{tar} -cf {archiveEsc}{verboseFlag} -T /dev/null";

        var parent = FileHelper.GetParentPath(sourceFullPaths[0]);
        foreach (var path in sourceFullPaths)
        {
            if (!string.Equals(FileHelper.GetParentPath(path), parent, StringComparison.Ordinal))
                throw new InvalidOperationException("All items to compress must be in the same folder.");
        }

        var parentEsc = ADBService.EscapeAdbShellString(parent);
        var printfArgs = string.Join(
            " ",
            sourceFullPaths.Select(p => ADBService.EscapeAdbShellString(FileHelper.GetFullName(p))));

        // Pack named members via -T so names with spaces survive; avoid "./name" members.
        // -v is its own token so tests can still match `tar -cf`.
        return $"cd {parentEsc} && printf '%s\\n' {printfArgs} | {tar} -cf {archiveEsc}{verboseFlag} -T -";
    }

    /// <summary>
    /// Creates a new tar-family archive at <paramref name="archivePath"/>.
    /// Compression is selected by toybox from the filename extension (e.g. <c>.tar.gz</c>).
    /// When <paramref name="sourceFullPaths"/> is empty, creates an empty archive.
    /// All sources must share the same parent directory.
    /// </summary>
    public static void CreateTarArchive(
        string deviceId,
        string archivePath,
        IReadOnlyList<string> sourceFullPaths,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTarMissing(deviceId);

        var script = BuildCreateTarArchiveScript(archivePath, sourceFullPaths);
        ExecuteTarCreateScript(deviceId, archivePath, script, cancellationToken);
    }

    /// <summary>
    /// Async counterpart of <see cref="CreateTarArchive"/> using
    /// <see cref="ADBService.ExecuteVoidShellCommand"/> so file-ops can show snackbar progress.
    /// Returns an empty string on success, or the error / <c>Canceled</c> text.
    /// </summary>
    public static Task<string> CreateTarArchiveAsync(
        string deviceId,
        string archivePath,
        IReadOnlyList<string> sourceFullPaths,
        CancellationToken cancellationToken = default,
        Action<string>? onVerboseMember = null)
    {
        try
        {
            ThrowIfTarMissing(deviceId);
            var verbose = onVerboseMember is not null;
            var script = BuildCreateTarArchiveScript(archivePath, sourceFullPaths, verbose);

            if (onVerboseMember is null)
                return RunTarCreateScriptAsync(deviceId, archivePath, script, cancellationToken);

            var callback = onVerboseMember;
            return Task.Run(
                () => RunTarCreateScriptStreaming(deviceId, archivePath, script, callback, cancellationToken),
                cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromResult(e.Message);
        }
    }

    /// <summary>
    /// Creates a gzip tar for an app backup. APKs are packed from <paramref name="apkParent"/>
    /// (names only, no <c>lib/</c> or <c>oat/</c>). Optional OBB is packed from
    /// <c>/sdcard/Android/obb</c> without copying into tmp. Uses <c>-z</c> so the archive can
    /// later be renamed to <c>.apkbkp</c> on Windows.
    /// </summary>
    public static void CreateApkBackupArchive(
        string deviceId,
        string archivePath,
        string apkParent,
        IReadOnlyList<string> apkFileNames,
        string? obbPackageName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTarMissing(deviceId);

        var tar = ShellCommands.TranslateCommand("tar");
        var script = AppBackupHelper.BuildCreateArchiveScript(
            tar, archivePath, apkParent, apkFileNames, obbPackageName);
        ExecuteTarCreateScript(deviceId, archivePath, script, cancellationToken);
    }

    /// <summary>
    /// Async counterpart of <see cref="CreateApkBackupArchive"/>. When
    /// <paramref name="onVerboseMember"/> is set, runs <c>tar -v</c> and invokes the
    /// callback for each member name on the same ADB stream (no extra polling).
    /// Returns an empty string on success, or the error / <c>Canceled</c> text.
    /// </summary>
    public static Task<string> CreateApkBackupArchiveAsync(
        string deviceId,
        string archivePath,
        string apkParent,
        IReadOnlyList<string> apkFileNames,
        string? obbPackageName,
        CancellationToken cancellationToken = default,
        Action<string>? onVerboseMember = null)
    {
        try
        {
            ThrowIfTarMissing(deviceId);
            var tar = ShellCommands.TranslateCommand("tar");
            var verbose = onVerboseMember is not null;
            var script = AppBackupHelper.BuildCreateArchiveScript(
                tar, archivePath, apkParent, apkFileNames, obbPackageName, verbose);

            if (onVerboseMember is null)
                return RunTarCreateScriptAsync(deviceId, archivePath, script, cancellationToken);

            var callback = onVerboseMember;
            return Task.Run(
                () => RunTarCreateScriptStreaming(deviceId, archivePath, script, callback, cancellationToken),
                cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromResult(e.Message);
        }
    }

    private static void ThrowIfTarMissing(string deviceId)
    {
        if (!ShellCommands.TarExists(deviceId))
            throw new InvalidOperationException("tar is not available on this device.");
    }

    private static void ExecuteTarCreateScript(
        string deviceId,
        string archivePath,
        string script,
        CancellationToken cancellationToken,
        Action<string>? onVerbose = null)
    {
        if (onVerbose is not null)
        {
            var streamed = RunTarCreateScriptStreaming(deviceId, archivePath, script, onVerbose, cancellationToken);
            if (streamed == "Canceled")
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException();
            }
            if (!string.IsNullOrEmpty(streamed))
                throw new IOException(streamed);
            return;
        }

        var exit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out var stderr,
            cancellationToken,
            "-c",
            ADBService.EscapeAdbShellString(script));

        if (exit == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        RemoveDeviceTree(deviceId, archivePath);
        throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private static async Task<string> RunTarCreateScriptAsync(
        string deviceId,
        string archivePath,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await ADBService.ExecuteVoidShellCommand(
            deviceId,
            cancellationToken,
            "sh",
            "-c",
            ADBService.EscapeAdbShellString(script)).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result) && result != "Canceled")
            RemoveDeviceTree(deviceId, archivePath);

        return result;
    }

    private static string RunTarCreateScriptStreaming(
        string deviceId,
        string archivePath,
        string script,
        Action<string> onVerboseMember,
        CancellationToken cancellationToken)
    {
        string lastError = "";
        try
        {
            var sh = ShellCommands.TranslateCommand("sh");
            foreach (var line in ADBService.ExecuteDeviceAdbCommandAsync(
                deviceId,
                "shell",
                cancellationToken,
                [sh, "-c", ADBService.EscapeAdbShellString(script)]))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("tar:", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = line;
                    continue;
                }

                onVerboseMember(line);
            }

            return "";
        }
        catch (OperationCanceledException)
        {
            RemoveDeviceTree(deviceId, archivePath);
            return "Canceled";
        }
        catch (ADBService.ProcessFailedException e)
        {
            RemoveDeviceTree(deviceId, archivePath);
            if (!string.IsNullOrWhiteSpace(e.StandardError))
                return e.StandardError.Trim();
            if (!string.IsNullOrWhiteSpace(lastError))
                return lastError;
            return e.Message;
        }
    }

    /// <summary>
    /// Extracts named tar members into <paramref name="destDir"/> (created if needed).
    /// Compression is autodetected from gzip magic (works for temp <c>.tar.gz</c>).
    /// </summary>
    public static void ExtractTarMembers(
        string deviceId,
        string archivePath,
        string destDir,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken = default,
        Action<string>? onVerbose = null)
    {
        if (members is null || members.Count == 0)
            throw new ArgumentException("At least one member is required.", nameof(members));

        ShellFileOperation.MakeDirs(deviceId, [destDir]).GetAwaiter().GetResult();

        var exitCode = ExtractTar(deviceId, archivePath, destDir, members, cancellationToken, out var stdout, out var stderr, onVerbose);
        if (exitCode == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Sizes for <c>tar -cvf</c> members created from <paramref name="sourceFullPaths"/>.
    /// Directories are expanded with a recursive listing; <c>stat</c> is not used as a directory size.
    /// </summary>
    public static Dictionary<string, long> CollectCreateMemberBytes(
        string deviceId,
        IReadOnlyList<string> sourceFullPaths,
        CancellationToken cancellationToken)
    {
        Dictionary<string, long> result = new(StringComparer.Ordinal);
        if (sourceFullPaths.Count == 0)
            return result;

        var parent = FileHelper.GetParentPath(sourceFullPaths[0]);
        foreach (var path in sourceFullPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ADBService.TryGetPathKind(deviceId, path, cancellationToken);
            if (kind is DevicePathKind.Directory)
                AddCreateDirectoryMembers(deviceId, parent, path, result, cancellationToken);
            else
                AddCreateFileMember(deviceId, path, result, cancellationToken);
        }

        return result;
    }

    private static void AddCreateFileMember(
        string deviceId,
        string path,
        Dictionary<string, long> result,
        CancellationToken cancellationToken)
    {
        var key = ArchiveVerboseProgress.NormalizeMember(FileHelper.GetFullName(path));
        if (string.IsNullOrEmpty(key))
            return;

        result[key] = ADBService.TryGetFileSize(deviceId, path, cancellationToken) ?? 0;
    }

    private static void AddCreateDirectoryMembers(
        string deviceId,
        string parent,
        string dirPath,
        Dictionary<string, long> result,
        CancellationToken cancellationToken)
    {
        var dirKey = ArchiveVerboseProgress.NormalizeMember(FileHelper.GetFullName(dirPath));
        if (!string.IsNullOrEmpty(dirKey))
            result.TryAdd(dirKey, 0);

        try
        {
            foreach (var entry in ADBService.ListDirectoryRecursive(deviceId, dirPath, cancellationToken))
            {
                var relative = FileHelper.ExtractRelativePath(entry.FullPath, parent, includeSelf: false);
                var key = ArchiveVerboseProgress.NormalizeMember(relative);
                if (string.IsNullOrEmpty(key))
                    continue;

                long size = 0;
                if (entry.Type is FileType.File)
                    size = entry.Size ?? 0;

                result[key] = size;
            }
        }
        catch
        {
            // Listing is best-effort; tar -v still reports members without sizes.
        }
    }

    private static int RunStreamingExtract(
        string deviceId,
        string command,
        IReadOnlyList<string> args,
        Action<string> onVerbose,
        CancellationToken cancellationToken,
        out string stdout,
        out string stderr)
    {
        stdout = "";
        stderr = "";
        string lastError = "";
        try
        {
            foreach (var line in ADBService.ExecuteDeviceAdbCommandAsync(
                deviceId,
                "shell",
                cancellationToken,
                [command, .. args]))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("tar:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("unzip:", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = line;
                    continue;
                }

                onVerbose(line);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stderr = "Canceled";
            return -1;
        }
        catch (ADBService.ProcessFailedException e)
        {
            if (!string.IsNullOrWhiteSpace(e.StandardError))
                stderr = e.StandardError.Trim();
            else if (!string.IsNullOrWhiteSpace(lastError))
                stderr = lastError;
            else
                stderr = e.Message;
            return e.ExitCode == 0 ? -1 : e.ExitCode;
        }
    }
}
