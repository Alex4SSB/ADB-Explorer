using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public static class Security
{
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

    public static Dictionary<string, string> CalculateAndroidFolderHash(FilePath path, Device device)
    {
        // find ./ -mindepth 1 -type f -exec md5sum {} \;
        string[] args = [ADBService.EscapeAdbShellString(path.FullPath), "-type", "f", "-exec", "md5sum", "{}", @"\;"];
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "find", out string stdout, out string stderr, new(), args);

        var list = AdbRegEx.RE_ANDROID_FIND_HASH().Matches(stdout);
        return list.Where(m => m.Success).ToDictionary(
            m => FileHelper.ExtractRelativePath(m.Groups["Path"].Value.TrimEnd('\r', '\n'), path.FullPath),
            m => m.Groups["Hash"].Value.ToUpper());
    }

    public static Dictionary<string, string> CalculateAndroidArchiveHash(FilePath path, Device device)
    {
        // tar -xf *.tar.gz --to-command='echo $(md5sum) $TAR_FILENAME'
        string[] args = ["xf", ADBService.EscapeAdbShellString(path.FullPath), "--to-command='echo $(md5sum) $TAR_FILENAME'"];
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "tar", out string stdout, out string stderr, new(), args);

        var list = AdbRegEx.RE_ANDROID_FIND_HASH().Matches(stdout);
        return list.Where(m => m.Success).ToDictionary(
            m => m.Groups["Path"].Value.TrimEnd('\r', '\n'),
            m => m.Groups["Hash"].Value.ToUpper());
    }

    public static void ValidateOps()
    {
        foreach (var item in Data.FileActions.SelectedFileOps.Value)
        {
            ValidateOperation(item);
        }
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
                source = (op.FilePath.PathType is AbstractFile.FilePathType.Android
                    ? CalculateAndroidFolderHash(op.FilePath, op.Device)
                    : CalculateWindowsFolderHash(op.FilePath.FullPath)).OrderBy(k => k.Key);
            },
            () =>
            {
                target = (op.TargetPath.PathType is AbstractFile.FilePathType.Android
                    ? CalculateAndroidFolderHash(op.TargetPath, op.Device)
                    : CalculateWindowsFolderHash(op.TargetPath.FullPath)).OrderBy(k => k.Key);
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
}
