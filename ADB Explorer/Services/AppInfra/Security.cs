using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using System.IO.Hashing;
using System.Security.Cryptography.X509Certificates;

namespace ADB_Explorer.Services;

public static class Security
{
    /// <summary>
    /// Verifies that the specified file has a valid Authenticode signature issued to Google LLC.
    /// </summary>
    /// <remarks>
    /// Uses WinVerifyTrust to check signature integrity, certificate chain trust (offline, no revocation check),
    /// and the certificate's owner.
    /// </remarks>
    public static bool VerifyAuthenticode(string filePath, string owner)
    {
        try
        {
            if (!NativeMethods.WinTrust.VerifyEmbeddedSignature(filePath))
                return false;

            using var cert = X509Certificate2.CreateFromSignedFile(filePath);

            return cert.Subject.Contains($"O={owner}", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies <paramref name="candidatePath"/> has a valid Authenticode signature whose
    /// certificate thumbprint matches <paramref name="referencePath"/>.
    /// </summary>
    public static bool VerifyAuthenticodeMatches(string referencePath, string candidatePath)
    {
        try
        {
            if (!NativeMethods.WinTrust.VerifyEmbeddedSignature(referencePath)
                || !NativeMethods.WinTrust.VerifyEmbeddedSignature(candidatePath))
            {
                return false;
            }

            using var referenceCert = X509Certificate.CreateFromSignedFile(referencePath);
            using var candidateCert = X509Certificate.CreateFromSignedFile(candidatePath);

            return string.Equals(
                referenceCert.GetCertHashString(),
                candidateCert.GetCertHashString(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string CalculateWindowsFileHash(string path, bool useSHA = false)
    {
        try
        {
            using StreamReader reader = new(path);
            return CalculateWindowsFileHash(reader.BaseStream, useSHA);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Calculates the cryptographic hash of the specified file stream using either the MD5 or SHA256 algorithm.
    /// </summary>
    /// <remarks>SHA256 provides a stronger hash than MD5 and is recommended for security-sensitive scenarios.
    /// The stream must not be modified during the hashing process.</remarks>
    /// <param name="file">A readable stream positioned at the beginning of the file to compute the hash for.</param>
    /// <param name="useSHA">Specifies whether to use the SHA256 algorithm. If <see langword="true"/>, SHA256 is used; otherwise, MD5 is
    /// used.</param>
    /// <returns>A hexadecimal *UPPERCASE* string representation of the computed hash value.</returns>
    public static string CalculateWindowsFileHash(Stream file, bool useSHA = false)
    {
        var hash = useSHA
            ? SHA256.HashData(file)
            : MD5.HashData(file);

        return Convert.ToHexString(hash);
    }

    public static string CalculateHexStringHash(string str)
    {
        var hash = MD5.HashData(Convert.FromHexString(str));
        return Convert.ToHexString(hash);
    }

    public static Dictionary<string, string> CalculateWindowsFolderHash(string path, string parent = "")
    {
        if (!Path.Exists(path))
            return [];

        if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            return new() { { Path.GetFileName(path), CalculateWindowsFileHash(path) } };
        }

        if (parent == "")
            parent = path;

        var folders = Directory.GetDirectories(path);
        var folderHashes = folders.AsParallel().SelectMany(f => CalculateWindowsFolderHash(f, parent)).AsEnumerable();

        var files = Directory.GetFiles(path);
        var fileHashes = files.AsParallel().ToDictionary(f => FileHelper.ExtractRelativePath(f, parent).Replace('\\', '/'), f => CalculateWindowsFileHash(f));

        return new(folderHashes.Concat(fileHashes));
    }

    /// <summary>IEEE CRC-32 as uppercase hex (matches Info-ZIP <c>unzip -lv</c>).</summary>
    public static string? CalculateWindowsFileCrc32(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var crc = new Crc32();
            crc.Append(stream);
            return crc.GetCurrentHashAsUInt32().ToString("X8");
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> CalculateWindowsFolderCrc32(string path, string parent = "")
    {
        if (!Path.Exists(path))
            return [];

        if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            var crc = CalculateWindowsFileCrc32(path);
            return crc is null ? [] : new() { { Path.GetFileName(path), crc } };
        }

        if (parent == "")
            parent = path;

        var folders = Directory.GetDirectories(path);
        var folderHashes = folders.AsParallel().SelectMany(f => CalculateWindowsFolderCrc32(f, parent)).AsEnumerable();

        var files = Directory.GetFiles(path);
        var fileHashes = files.AsParallel()
            .Select(f => (Key: FileHelper.ExtractRelativePath(f, parent).Replace('\\', '/'), Crc: CalculateWindowsFileCrc32(f)))
            .Where(x => x.Crc is not null)
            .ToDictionary(x => x.Key, x => x.Crc!);

        return new(folderHashes.Concat(fileHashes));
    }

    /// <summary>CRC-32 values from cached <c>unzip -lv</c> TOC, keyed relative to the selection.</summary>
    public static Dictionary<string, string> GetZipArchiveCrc32(
        string deviceId,
        string archivePath,
        string? internalPath = null,
        bool isDirectory = false)
    {
        var toc = ArchiveListing.GetOrFetchToc(deviceId, archivePath, CancellationToken.None);
        return GetZipCrc32FromEntries(toc.Entries, internalPath, isDirectory);
    }

    internal static Dictionary<string, string> GetZipCrc32FromEntries(
        IReadOnlyList<ArchiveEntry> entries,
        string? internalPath = null,
        bool isDirectory = false)
    {
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Crc))
                continue;

            raw[entry.Path.Replace('\\', '/')] = entry.Crc.Trim().ToUpperInvariant();
        }

        return RelativizeArchiveHashes(raw, internalPath, isDirectory);
    }

    /// <summary>
    /// On-device folder/file hashes via <c>find … -exec cksum -HNPL|md5sum</c>.
    /// Keys are relative to <paramref name="path"/> (basename for a single file).
    /// </summary>
    public static Dictionary<string, string> CalculateAndroidFolderHash(
        FilePath path,
        string deviceId,
        ValidationHashMode mode = ValidationHashMode.Md5)
    {
        var hashCmd = mode switch
        {
            ValidationHashMode.Crc32 => ShellCommands.GetCrc32Command(deviceId) ?? "cksum -HNPL",
            ValidationHashMode.Md5 => ShellCommands.GetMd5SumCommand(deviceId) ?? "md5sum",
            _ => null,
        };
        if (hashCmd is null)
            return [];

        // cksum -HNPL / md5sum both print "HASH PATH".
        string[] args =
            [ADBService.EscapeAdbShellString(path.FullPath), "-type", "f", "-exec", .. hashCmd.Split(' '), "{}", @"\;"];

        ADBService.ExecuteDeviceAdbShellCommand(deviceId, "find", out string stdout, out _, new(), args);

        var list = AdbRegEx.RE_ANDROID_FIND_HASH().Matches(stdout);
        return list.Where(m => m.Success).ToDictionary(
            m => FileHelper.ExtractRelativePath(m.Groups["Path"].Value.TrimEnd('\r', '\n'), path.FullPath),
            m => m.Groups["Hash"].Value.ToUpperInvariant());
    }

    /// <summary>
    /// Archive member hashes via <c>tar</c> pipeline with shell <c>cksum -HNPL</c> or <c>md5sum</c>.
    /// Prefers <c>--to-command</c> when supported; otherwise <c>-xOf</c> (toybox/GNU <c>-O</c>).
    /// When <paramref name="memberPaths"/> is set, only those members (and their descendants for directories) are hashed.
    /// Keys are paths relative to the selection root (basename for a single file).
    /// </summary>
    public static Dictionary<string, string> CalculateAndroidArchiveHash(
        string archivePath,
        string deviceId,
        string? internalPath = null,
        bool isDirectory = false,
        IEnumerable<string>? memberPaths = null,
        ValidationHashMode mode = ValidationHashMode.Md5)
    {
        var hashCmd = mode switch
        {
            ValidationHashMode.Crc32 => ShellCommands.GetCrc32Command(deviceId) ?? "cksum -HNPL",
            ValidationHashMode.Md5 => ShellCommands.GetMd5SumCommand(deviceId) ?? "md5sum",
            _ => null,
        };
        if (hashCmd is null)
            return [];

        if (ShellCommands.TarToCommandSupported(deviceId))
            return HashArchiveViaToCommand(archivePath, deviceId, internalPath, isDirectory, memberPaths, hashCmd);

        if (ShellCommands.TarToStdoutSupported(deviceId))
            return HashArchiveViaStdout(archivePath, deviceId, internalPath, isDirectory, memberPaths, hashCmd);

        return [];
    }

    private static Dictionary<string, string> HashArchiveViaToCommand(
        string archivePath,
        string deviceId,
        string? internalPath,
        bool isDirectory,
        IEnumerable<string>? memberPaths,
        string hashCmd)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        // Hash tools read stdin when no file args; echo "HASH PATH" for RE_ANDROID_FIND_HASH.
        var args = new List<string>
        {
            "xf",
            ADBService.EscapeAdbShellString(archivePath),
            $"--to-command=echo $({hashCmd}) $TAR_FILENAME",
        };

        if (memberPaths is not null)
            args.AddRange(memberPaths.Select(m => ADBService.EscapeAdbShellString(m.TrimEnd('/'))));
        else if (!string.IsNullOrEmpty(internalPath))
            args.Add(ADBService.EscapeAdbShellString(internalPath));

        ADBService.ExecuteDeviceAdbShellCommand(deviceId, tar, out string stdout, out _, new(), [.. args]);
        return ParseArchiveHashStdout(stdout, internalPath, isDirectory);
    }

    /// <summary>
    /// Per-member <c>tar -xOf archive member | hash</c> (toybox/GNU <c>-O</c> extract to stdout).
    /// </summary>
    private static Dictionary<string, string> HashArchiveViaStdout(
        string archivePath,
        string deviceId,
        string? internalPath,
        bool isDirectory,
        IEnumerable<string>? memberPaths,
        string hashCmd)
    {
        var tar = ShellCommands.TranslateCommand("tar");
        var archiveEsc = ADBService.EscapeAdbShellString(archivePath);

        IEnumerable<string> members = memberPaths
            ?? (!string.IsNullOrEmpty(internalPath) ? [internalPath] : []);

        var fileMembers = members
            .Where(m => !string.IsNullOrEmpty(m) && !m.EndsWith('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (fileMembers.Count == 0)
            return [];

        // One shell round-trip: for each member, extract to stdout and hash.
        var parts = fileMembers.Select(m =>
        {
            var memberEsc = ADBService.EscapeAdbShellString(m);
            return $"echo $({tar} -xOf {archiveEsc} {memberEsc} | {hashCmd}) {memberEsc}";
        });

        var script = string.Join("; ", parts);
        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out string stdout,
            out _,
            new(),
            "-c",
            ADBService.EscapeAdbShellString(script));

        return ParseArchiveHashStdout(stdout, internalPath, isDirectory);
    }

    private static Dictionary<string, string> ParseArchiveHashStdout(
        string stdout,
        string? internalPath,
        bool isDirectory)
    {
        var list = AdbRegEx.RE_ANDROID_FIND_HASH().Matches(stdout);
        var raw = list.Where(m => m.Success).ToDictionary(
            m => m.Groups["Path"].Value.TrimEnd('\r', '\n').Replace('\\', '/').Trim('"'),
            m => m.Groups["Hash"].Value.ToUpperInvariant(),
            StringComparer.Ordinal);

        return RelativizeArchiveHashes(raw, internalPath, isDirectory);
    }

    private static Dictionary<string, string> RelativizeArchiveHashes(
        Dictionary<string, string> raw,
        string? internalPath,
        bool isDirectory)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        if (string.IsNullOrEmpty(internalPath))
            return raw;

        if (!isDirectory)
        {
            // Single file: Windows side keys by basename.
            if (raw.TryGetValue(internalPath, out var hash))
                return new() { { FileHelper.GetFullName(internalPath), hash } };

            // Some tars emit only the basename in $TAR_FILENAME.
            var baseName = FileHelper.GetFullName(internalPath);
            if (raw.TryGetValue(baseName, out hash))
                return new() { { baseName, hash } };

            return raw.Count == 1
                ? new() { { baseName, raw.Values.First() } }
                : [];
        }

        var prefix = internalPath + "/";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, hash) in raw)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                result[path[prefix.Length..]] = hash;
            else if (path.Equals(internalPath, StringComparison.Ordinal))
                continue; // directory marker
        }

        return result;
    }

    public static async void ValidateOperation(FileOperation op)
    {
        IOrderedEnumerable<KeyValuePair<string, string>> source = null, target = null;
        op.SetValidation(true);

        await Task.Run(() =>
        {
            Parallel.Invoke(
            () =>
            {
                source = GetValidationHashes(op, isSource: true).OrderBy(k => k.Key);
            },
            () =>
            {
                target = GetValidationHashes(op, isSource: false).OrderBy(k => k.Key);
            });
        });

        op.ClearChildren();
        var fails = 0;
        FileOpProgressInfo update = null;

        foreach (var item in source)
        {
            var key = item.Key;
            var other = target.Where(f => f.Key == item.Key ||
                                    (op.OperationName is FileOperation.OperationType.Copy
                                    && target.Count() == 1));

            key = op.AndroidPath.IsDirectory
                ? FileHelper.ConcatPaths(op.AndroidPath, key)
                : op.AndroidPath.FullPath;

            if (!other.Any())
                update = new HashFailInfo(key, false);
            else if (item.Value.Equals(other.First().Value))
                update = new HashSuccessInfo(key);
            else
                update = new HashFailInfo(key);

            op.AddUpdates(update);

            if (update is HashFailInfo)
                fails++;
        }

        var message = "";
        if (source.Count() == 1)
        {
            message = update is HashFailInfo fail
                ? string.Format(Strings.Resources.S_VALIDATION_ERROR, fail.Message)
                : Strings.Resources.S_FILEOP_VALIDATED;
        }
        else
        {
            message = FileOpStatusConverter.StatusString(typeof(HashFailInfo), source.Count() - fails, fails, total: true);
        }

        op.StatusInfo = fails > 0
            ? new FailedOpProgressViewModel(message)
            : new CompletedShellProgressViewModel(message);

        op.IsValidated = fails < 1;
        op.SetValidation(false);
    }

    private static Dictionary<string, string> GetValidationHashes(FileOperation op, bool isSource)
    {
        var mode = ShellCommands.GetValidationHashMode(op.Device.ID);
        var androidDest = op.TargetPath?.PathType is AbstractFile.FilePathType.Android;

        if (op.TryGetArchiveValidationSource(out var archivePath, out var internalPath, out var isDirectory)
            && ArchiveHelper.SupportsHashValidation(archivePath, op.Device.ID, androidDest))
        {
            var useCrc = ArchiveHelper.UsesCrc32Validation(archivePath, op.Device.ID, androidDest);
            var hashMode = useCrc ? ValidationHashMode.Crc32 : ValidationHashMode.Md5;

            if (isSource)
            {
                // Zip TOC CRC matches IEEE CRC-32 (same as cksum -HNPL / Windows Crc32).
                if (ArchiveHelper.GetFamily(archivePath) is ArchiveFamily.Zip && useCrc)
                    return GetZipArchiveCrc32(op.Device.ID, archivePath, internalPath, isDirectory);

                IEnumerable<string>? members = null;
                if (!string.IsNullOrEmpty(internalPath))
                {
                    var toc = ArchiveListing.GetOrFetchToc(op.Device.ID, archivePath, CancellationToken.None);
                    members = ArchiveExtract.GetMemberPathsToExtract(toc.Entries, internalPath, isDirectory);
                }

                return CalculateAndroidArchiveHash(
                    archivePath, op.Device.ID, internalPath, isDirectory, members, hashMode);
            }

            // Destination side: Windows pull target, or on-device extract target.
            if (op.TargetPath.PathType is AbstractFile.FilePathType.Windows)
            {
                return useCrc
                    ? CalculateWindowsFolderCrc32(op.TargetPath.FullPath)
                    : CalculateWindowsFolderHash(op.TargetPath.FullPath);
            }

            return CalculateAndroidFolderHash(op.TargetPath, op.Device.ID, hashMode);
        }

        if (mode is ValidationHashMode.None)
            return [];

        var path = isSource ? op.FilePath : op.TargetPath;
        if (path.PathType is AbstractFile.FilePathType.Android)
            return CalculateAndroidFolderHash(path, op.Device.ID, mode);

        return mode is ValidationHashMode.Crc32
            ? CalculateWindowsFolderCrc32(path.FullPath)
            : CalculateWindowsFolderHash(path.FullPath);
    }
}
