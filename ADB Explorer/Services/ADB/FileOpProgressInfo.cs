using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public abstract class FileOpProgressInfo : ViewModelBase
{
    public string AndroidPath { get; protected set; }

    public void SetPathToCurrent(FileOperation op)
    {
        string currentPath = ((InProgSyncProgressViewModel)op.StatusInfo).CurrentFilePath;
        if (string.IsNullOrEmpty(currentPath))
            currentPath = FileHelper.ConcatPaths(op.TargetPath, op.FilePath.FullName);

        AndroidPath = currentPath;
    }
}

public abstract class FileOpErrorInfo : FileOpProgressInfo
{
    public string Message { get; protected set; }

    protected FileOpErrorInfo(string message)
    {
        Message = message.TrimEnd('\r', '\n');
    }

    protected FileOpErrorInfo()
    {

    }
}

public class HashFailInfo : FileOpErrorInfo
{
    public HashFailInfo(string androidPath, bool fileExists = true)
        : base(fileExists ? Strings.Resources.S_VALIDATE_MISMATCH : Strings.Resources.S_VALIDATE_MISSING)
    {
        AndroidPath = androidPath;
    }
}

public class HashSuccessInfo : FileOpProgressInfo
{
    public HashSuccessInfo(string androidPath)
    {
        AndroidPath = androidPath;
    }
}

public class ShellErrorInfo : FileOpErrorInfo
{
    public ShellErrorInfo(Match match, string parentPath)
        : base(match.Groups["Message"].Value)
    {
        AndroidPath = match.Groups["AndroidPath"].Value;

        if (!AndroidPath.StartsWith('/'))
            AndroidPath = FileHelper.ConcatPaths(parentPath, AndroidPath);
    }

    public ShellErrorInfo(string message, string androidPath)
        : base(message)
    {
        AndroidPath = androidPath;
    }
}

public class SyncErrorInfo : FileOpErrorInfo
{
    public string WindowsPath { get; protected set; }

    private SyncErrorInfo()
    {

    }

    public static SyncErrorInfo New(Match match)
    {
        SyncErrorInfo result = new();

        if (match.Groups["AndroidPath"].Success)
            result.AndroidPath = match.Groups["AndroidPath"].Value;
        else if (match.Groups["AndroidPath1"].Success)
            result.AndroidPath = match.Groups["AndroidPath1"].Value;

        if (match.Groups["WindowsPath"].Success)
            result.WindowsPath = match.Groups["WindowsPath"].Value;
        else if (match.Groups["WindowsPath1"].Success)
            result.WindowsPath = match.Groups["WindowsPath1"].Value;

        if (string.IsNullOrEmpty(result.AndroidPath) && string.IsNullOrEmpty(result.WindowsPath))
            return null;

        result.Message = match.Groups["Message"].Value;
        
        var path = string.IsNullOrEmpty(result.WindowsPath) ? result.AndroidPath : result.WindowsPath;

        if (path.Any(c => AdbExplorerConst.INVALID_NTFS_CHARS.Any(chr => chr == c)))
        {
            result.Message = string.Format(Strings.Resources.S_NAME_ILLEGAL_FS, FileHelper.GetFullName(path));
        }
        else if (result.Message.Contains(':'))
            result.Message = result.Message.Split(':').Last().Trim();

        return result;
    }
}

public class AdbSyncProgressInfo : FileOpProgressInfo
{
    public int? TotalPercentage { get; set; }
    public int? CurrentFilePercentage { get; }
    public UInt64? CurrentFileBytesTransferred { get; }
    public ulong? TotalBytesTransferred { get; }

    public AdbSyncProgressInfo(string currentFile, int? totalPercentage, int? currentFilePercentage, ulong? currentFileBytesTransferred)
    {
        AndroidPath = currentFile;
        TotalPercentage = totalPercentage;

        if (currentFilePercentage is null)
            TotalBytesTransferred = currentFileBytesTransferred;
        else
        {
            CurrentFilePercentage = currentFilePercentage;
            CurrentFileBytesTransferred = currentFileBytesTransferred;
        }
    }
}

public class AdbSyncStatsInfo
{
    public string SourcePath { get; }
    public UInt64 FilesTransferred { get; }
    public UInt64 FilesSkipped { get; }

    /// <summary>
    /// Rate of transfer in MB/s
    /// </summary>
    public decimal? AverageRate { get; }
    public UInt64? TotalBytes { get; }

    /// <summary>
    /// Transfer time in seconds
    /// </summary>
    public decimal? TotalTime { get; }

    public AdbSyncStatsInfo(string sourcePath, ulong? totalBytes, decimal? totalTime, ulong filesTransferred = 1, ulong filesSkipped = 0, decimal? averageRate = -1)
    {
        SourcePath = sourcePath;
        TotalBytes = totalBytes;
        TotalTime = totalTime;

        FilesTransferred = filesTransferred;
        FilesSkipped = filesSkipped;

        if (averageRate == -1 && totalBytes.HasValue && totalTime.HasValue && totalTime > 0)
            AverageRate = totalBytes.Value / 1000000 / totalTime.Value;
        else
            AverageRate = averageRate;
    }
}
