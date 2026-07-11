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
    public const string StagingFolderName = "adb-explorer-extract";

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
            || !stagingRoot.StartsWith($"{AdbExplorerConst.TEMP_PATH}/{StagingFolderName}", StringComparison.Ordinal))
            return;

        ActiveStagingRoots.TryRemove(stagingRoot, out _);

        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "rm",
            out _,
            out _,
            cancellationToken,
            "-rf",
            ADBService.EscapeAdbShellString(stagingRoot));
    }

    /// <summary>Removes any extract staging left from a previous clipboard/drag that was never consumed.</summary>
    public static void CleanupAllStaging(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        deviceId ??= Data.DevicesObject?.Current?.ID;
        if (deviceId is null)
        {
            ActiveStagingRoots.Clear();
            return;
        }

        foreach (var root in ActiveStagingRoots.Keys.ToArray())
            CleanupStaging(deviceId, root, cancellationToken);
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
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, cancellationToken);
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

        var exitCode = family switch
        {
            ArchiveFamily.Tar => ExtractTar(deviceId, archivePath, contentRoot, members, cancellationToken),
            ArchiveFamily.Zip => ExtractZip(deviceId, archivePath, contentRoot, members, cancellationToken),
            _ => -1,
        };

        if (exitCode != 0)
        {
            // ExecuteCommand returns -1 on cancel instead of throwing; don't surface that as extract failure.
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException($"Failed to extract from {archivePath}");
        }
    }

    private static int ExtractTar(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        var args = new List<string>
        {
            "-xf",
            ADBService.EscapeAdbShellString(archivePath),
            "-C",
            ADBService.EscapeAdbShellString(contentRoot),
        };
        foreach (var member in members)
            args.Add(ADBService.EscapeAdbShellString(member.TrimEnd('/')));

        return ADBService.ExecuteDeviceAdbShellCommand(deviceId, tar, out _, out _, cancellationToken, [.. args]);
    }

    private static int ExtractZip(
        string deviceId,
        string archivePath,
        string contentRoot,
        IReadOnlyList<string> members,
        CancellationToken cancellationToken)
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

        return ADBService.ExecuteDeviceAdbShellCommand(deviceId, unzip, out _, out _, cancellationToken, [.. args]);
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
