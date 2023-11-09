using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public static class Security
{
    public static string CalculateWindowsFileHash(string path)
        => CalculateWindowsFileHash(new StreamReader(path).BaseStream);

    public static string CalculateWindowsFileHash(Stream file)
    {
        var hash = MD5.HashData(file);
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
            return new();

        if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            return new() { { Path.GetFileName(path), CalculateWindowsFileHash(path) } };
        }

        if (parent == "")
            parent = path;

        var folders = Directory.GetDirectories(path);
        var folderHashes = folders.AsParallel().SelectMany(f => CalculateWindowsFolderHash(f, parent)).AsEnumerable();

        var files = Directory.GetFiles(path);
        var fileHashes = files.AsParallel().ToDictionary(f => FileHelper.ExtractRelativePath(f, parent).Replace('\\', '/'), CalculateWindowsFileHash);
        
        return new(folderHashes.Concat(fileHashes));
    }

    public static Dictionary<string, string> CalculateAndroidFolderHash(FilePath path, Device device)
    {
        // find ./ -mindepth 1 -type f -exec md5sum {} \;
        string[] args = { ADBService.EscapeAdbShellString(path.FullPath), "-type", "f", "-exec", "md5sum", "{}", @"\;" };
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "find", out string stdout, out string stderr, args);

        var list = AdbRegEx.RE_ANDROID_FIND_HASH.Matches(stdout);
        return list.Where(m => m.Success).ToDictionary(
            m => FileHelper.ExtractRelativePath(m.Groups["Path"].Value.TrimEnd('\r', '\n'), path.FullPath), 
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
            var other = target.Where(f => f.Key == item.Key);

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
            message = update is HashFailInfo fail ? "Validation error: " + fail.Message : "Validated";
        }
        else
        {
            message = FileOpStatusConverter.StatusString(typeof(HashFailInfo), source.Count() - fails, fails, total: true);
        }

        op.StatusInfo = fails > 0
            ? new FailedOpProgressViewModel(message)
            : new CompletedShellProgressViewModel(message);

        op.SetValidation(false);
    }
}
