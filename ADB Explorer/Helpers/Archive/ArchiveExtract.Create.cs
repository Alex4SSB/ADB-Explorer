using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

public static partial class ArchiveExtract
{
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
        var archiveEsc = AdbService.EscapeAdbShellString(archivePath);
        var verboseFlag = verbose ? " -v" : "";

        if (sourceFullPaths.Count == 0)
            return $"{tar} -cf {archiveEsc}{verboseFlag} -T /dev/null";

        var parent = FileHelper.GetParentPath(sourceFullPaths[0]);
        foreach (var path in sourceFullPaths)
        {
            if (!string.Equals(FileHelper.GetParentPath(path), parent, StringComparison.Ordinal))
                throw new InvalidOperationException("All items to compress must be in the same folder.");
        }

        var parentEsc = AdbService.EscapeAdbShellString(parent);
        var printfArgs = string.Join(
            " ",
            sourceFullPaths.Select(p => AdbService.EscapeAdbShellString(FileHelper.GetFullName(p))));

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
    /// <see cref="AdbService.ExecuteVoidShellCommand"/> so file-ops can show snackbar progress.
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

        var exit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out var stderr,
            cancellationToken,
            "-c",
            AdbService.EscapeAdbShellString(script));

        if (exit == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        RemoveDeviceTree(deviceId, archivePath);
        throw new IOException(AdbService.ErrorOutput(stdout, stderr));
    }

    private static async Task<string> RunTarCreateScriptAsync(
        string deviceId,
        string archivePath,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await AdbService.ExecuteVoidShellCommand(
            deviceId,
            cancellationToken,
            "sh",
            "-c",
            AdbService.EscapeAdbShellString(script)).ConfigureAwait(false);

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
            foreach (var line in AdbService.ExecuteDeviceAdbCommandAsync(
                deviceId,
                "shell",
                cancellationToken,
                [sh, "-c", AdbService.EscapeAdbShellString(script)]))
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
        catch (AdbService.ProcessFailedException e)
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
            var kind = AdbService.TryGetPathKind(deviceId, path, cancellationToken);
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

        result[key] = AdbService.TryGetFileSize(deviceId, path, cancellationToken) ?? 0;
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
            foreach (var entry in AdbService.ListDirectoryRecursive(deviceId, dirPath, cancellationToken))
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
}
