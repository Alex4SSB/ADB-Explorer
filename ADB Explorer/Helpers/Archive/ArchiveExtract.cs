using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Extracts archive members to a real device path for paste / pull staging.
/// Files are flattened to their basename; directories keep their internal tree under the selected folder name.
/// </summary>
public static partial class ArchiveExtract
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

        AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "rm",
            out _,
            out _,
            CancellationToken.None,
            "-rf",
            AdbService.EscapeAdbShellString(path));
    }

    /// <summary>
    /// Deletes every staging folder under <see cref="AdbExplorerConst.TEMP_PATH"/> matching the
    /// current (and legacy) name prefix — used on app shutdown.
    /// </summary>
    public static void CleanupAllStaging(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        deviceId ??= Data.ActiveDevice?.ID;
        ActiveStagingRoots.Clear();

        if (deviceId is null)
            return;

        // Glob wipe: tracked and orphaned dirs (e.g. after a crash).
        var script = $"rm -rf {AdbExplorerConst.TEMP_PATH}/{StagingFolderName}-*";

        AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out _,
            out _,
            CancellationToken.None,
            "-c",
            AdbService.EscapeAdbShellString(script));
    }

    /// <summary>
    /// Fire-and-forget cleanup of currently tracked staging roots only
    /// (clipboard/drag lifecycle — avoids glob-wiping a newly created root).
    /// </summary>
    public static void BeginCleanupAllStaging(string? deviceId = null)
    {
        deviceId ??= Data.ActiveDevice?.ID;

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
            AdbService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "rm",
                out _,
                out _,
                cancellationToken,
                "-rf",
                AdbService.EscapeAdbShellString(destinationPath));

            var moveResult = AdbService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                AdbService.EscapeAdbShellString(extractedPath),
                AdbService.EscapeAdbShellString(destinationPath));

            AdbService.ThrowIfFailed(moveResult, stdout, stderr);

            RemoveDeviceTree(deviceId, contentRoot);
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

            var moveResult = AdbService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "mv",
                out var stdout,
                out var stderr,
                cancellationToken,
                AdbService.EscapeAdbShellString(extractedContent),
                AdbService.EscapeAdbShellString(extractedPath));

            AdbService.ThrowIfFailed(moveResult, stdout, stderr, cancellationToken);

            // Pull reads from out/; drop the tar/unzip tree under content/ immediately.
            RemoveDeviceTree(deviceId, contentRoot);

            // Prefer listing the extracted filesystem so nested dirs are not mistaken for empty files.
            var tree = isDirectory
                ? FileHelper.GetFolderTree([extractedPath], cancellationToken: cancellationToken, deviceId: deviceId)
                : [];

            return (stagingRoot, extractedPath, tree);
        }
        catch
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Extracts <paramref name="item"/> (an archive member) to staging and returns it as a pullable
    /// <see cref="SyncFile"/> under its own name. Caller must <see cref="CleanupStaging"/> the root.
    /// </summary>
    public static (string StagingRoot, SyncFile Source) ExtractSelectionAsSyncFile(
        string deviceId,
        string archivePath,
        string internalPath,
        FileClass item,
        CancellationToken cancellationToken = default)
    {
        var (stagingRoot, extractedPath, tree) = ExtractSelectionForPull(deviceId, archivePath, internalPath, item.IsDirectory, cancellationToken);
        var extracted = new FileClass(item.FullName, extractedPath, item.Type, size: item.Size, modifiedTime: item.ModifiedTime);

        return (stagingRoot, new SyncFile(extracted, tree));
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
            var detail = AdbService.ErrorOutput(stdout, stderr);
            throw new IOException(string.IsNullOrWhiteSpace(detail)
                ? $"Failed to extract from {archivePath}"
                : $"Failed to extract from {archivePath}: {detail.Trim()}");
        }
    }

    /// <summary>Extracts <paramref name="candidateMembers"/> to a temp staging dir and greps them for
    /// <paramref name="query"/> (literal); returns the internal paths of members whose content matched.</summary>
    public static IReadOnlyList<string> SearchMemberContents(
        string deviceId,
        ArchiveFamily family,
        string archivePath,
        IReadOnlyList<string> candidateMembers,
        string query,
        bool caseSensitive,
        CancellationToken cancellationToken)
    {
        if (candidateMembers.Count == 0)
            return [];

        var stagingRoot = CreateStagingRoot(deviceId, cancellationToken);
        var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");

        try
        {
            ShellFileOperation.MakeDirs(deviceId, [contentRoot]).GetAwaiter().GetResult();

            if (family is ArchiveFamily.Zip)
            {
                ExtractZipMembersInto(deviceId, archivePath, contentRoot, candidateMembers, cancellationToken, allowMissingMembers: true);
            }
            else
            {
                var toc = ArchiveListing.GetOrFetchToc(deviceId, archivePath, cancellationToken);
                var members = candidateMembers;
                if (toc.UsesDotSlashPrefix)
                    members = [.. members.Select(m => m.StartsWith("./", StringComparison.Ordinal) ? m : "./" + m)];

                ExtractTar(deviceId, archivePath, contentRoot, members, cancellationToken, out _, out _);
            }

            var grep = ShellCommands.TranslateCommand("grep");
            string[] caseArg = caseSensitive ? [] : ["-i"];
            string[] grepArgs =
            [
                "-r", "-l", "-I", "-F",
                .. caseArg,
                "--", AdbService.EscapeAdbShellString(query),
                AdbService.EscapeAdbShellString(contentRoot),
                "2>/dev/null",
            ];

            var prefix = contentRoot.TrimEnd('/') + "/";
            List<string> matchedMembers = [];

            foreach (var line in AdbService.ExecuteDeviceAdbCommandAsync(deviceId, "shell", cancellationToken, [grep, .. grepArgs]))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var staged = line.Trim();
                if (staged.StartsWith(prefix, StringComparison.Ordinal))
                    matchedMembers.Add(ArchivePath.NormalizeInternal(staged[prefix.Length..]));
            }

            return matchedMembers;
        }
        finally
        {
            CleanupStaging(deviceId, stagingRoot, CancellationToken.None);
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
            AdbService.EscapeAdbShellString(archivePath),
            "-C",
            AdbService.EscapeAdbShellString(contentRoot),
        };
        foreach (var member in members)
            args.Add(AdbService.EscapeAdbShellString(member.TrimEnd('/')));

        if (onVerbose is not null)
            return RunStreamingExtract(deviceId, tar, args, onVerbose, cancellationToken, out stdout, out stderr);

        return AdbService.ExecuteDeviceAdbShellCommand(deviceId, tar, out stdout, out stderr, cancellationToken, [.. args]);
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

        throw new IOException(AdbService.ErrorOutput(stdout, stderr));
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

        args.Add(AdbService.EscapeAdbShellString(archivePath));
        args.Add("-d");
        args.Add(AdbService.EscapeAdbShellString(contentRoot));
        args.AddRange(members.Select(m => AdbService.EscapeAdbShellString(m)));

#if DEBUG
        var quiet = onVerbose is null ? " -q" : "";
        ApkIconService.MarkLoadStep($"ExtractZip (unzip -o{quiet}) {members.Count} member(s)");
#endif
        if (onVerbose is not null)
            return RunStreamingExtract(deviceId, unzip, args, onVerbose, cancellationToken, out stdout, out stderr);

        return AdbService.ExecuteDeviceAdbShellCommand(deviceId, unzip, out stdout, out stderr, cancellationToken, [.. args]);
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
        throw new IOException(AdbService.ErrorOutput(stdout, stderr));
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
            foreach (var line in AdbService.ExecuteDeviceAdbCommandAsync(
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
        catch (AdbService.ProcessFailedException e)
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
