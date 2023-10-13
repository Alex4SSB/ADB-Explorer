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
        if (parent == "")
            parent = path;

        var folders = Directory.GetDirectories(path);
        var folderHashes = folders.AsParallel().SelectMany(f => CalculateWindowsFolderHash(f, parent)).AsEnumerable();

        var files = Directory.GetFiles(path);
        var fileHashes = files.AsParallel().ToDictionary(f => FileHelper.ExtractRelativePath(f, parent).Replace('\\', '/'), CalculateWindowsFileHash);
        
        return new(folderHashes.Concat(fileHashes));
    }

    public static Dictionary<string, string> CalculateAndroidFolderHash(string path)
    {
        // find ./ -mindepth 1 -type f -exec md5sum {} \;
        string[] args = { ADBService.EscapeAdbShellString(path), "-mindepth", "1", "-type", "f", "-exec", "md5sum", "{}", @"\;" };
        ADBService.ExecuteDeviceAdbShellCommand(Data.CurrentADBDevice.ID, "find", out string stdout, out string stderr, args);

        var list = AdbRegEx.RE_ANDROID_FIND_HASH.Matches(stdout);
        return list.Where(m => m.Success).ToDictionary(
            m => FileHelper.ExtractRelativePath(m.Groups["Path"].Value.TrimEnd('\r', '\n'), path), 
            m => m.Groups["Hash"].Value.ToUpper());
    }

    public static async void ValidateOperation()
    {
        IOrderedEnumerable<KeyValuePair<string, string>> source = null, target = null;
        var op = Data.FileActions.SelectedFileOp;
        op.SetValidation(true);

        await Task.Run(() =>
        {
            Parallel.Invoke(
            () =>
            {
                source = (op.FilePath.PathType is AbstractFile.FilePathType.Android
                    ? CalculateAndroidFolderHash(op.FilePath.FullPath)
                    : CalculateWindowsFolderHash(op.FilePath.FullPath)).OrderBy(k => k.Key);
            },
            () =>
            {
                target = (op.TargetPath.PathType is AbstractFile.FilePathType.Android
                    ? CalculateAndroidFolderHash(op.FullTargetItemPath)
                    : CalculateWindowsFolderHash(op.FullTargetItemPath)).OrderBy(k => k.Key);
            });
        });
        
        op.ClearChildren();
        var fails = 0;

        foreach (var item in source)
        {
            var key = item.Key;
            var other = target.Where(f => f.Key == item.Key);
            FileOpProgressInfo update = null;

            key = FileHelper.ConcatPaths(op.AndroidPath.FullPath, key);

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

        var message = FileOpStatusConverter.StatusString(typeof(HashFailInfo), source.Count() - fails, fails, total: true);
        op.StatusInfo = fails > 0
            ? new FailedOpProgressViewModel(message)
            : new CompletedShellProgressViewModel(message);

        op.SetValidation(false);
    }
}
