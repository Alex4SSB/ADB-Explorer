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
}

public class SyncErrorInfo : FileOpErrorInfo
{
    public string WindowsPath { get; protected set; }

    private SyncErrorInfo()
    {

    }
}

public class AdbSyncProgressInfo : FileOpProgressInfo
{
    public int? TotalPercentage { get; set; }
    public int? CurrentFilePercentage { get; }
    public long? CurrentFileBytesTransferred { get; }
    public long? TotalBytesTransferred { get; }

    public long? BytesTransferred => CurrentFileBytesTransferred ?? TotalBytesTransferred;

    public AdbSyncProgressInfo(string currentFile, int? totalPercentage, int? currentFilePercentage, long? currentFileBytesTransferred)
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
    public int FilesTransferred { get; }
    public int FilesSkipped { get; }

    /// <summary>
    /// Rate of transfer in MB/s
    /// </summary>
    public double? AverageRate { get; }
    public long? TotalBytes { get; }

    /// <summary>
    /// Transfer time in seconds
    /// </summary>
    public double? TotalTime { get; }

    public AdbSyncStatsInfo(string sourcePath, long? totalBytes, double? totalTime, int filesTransferred = 1, int filesSkipped = 0, double? averageRate = -1)
    {
        SourcePath = sourcePath;
        TotalBytes = totalBytes;
        TotalTime = totalTime;

        FilesTransferred = filesTransferred;
        FilesSkipped = filesSkipped;

        if (averageRate == -1 && totalBytes.HasValue && totalTime.HasValue && totalTime > 0)
            AverageRate = totalBytes.Value / 1000000.0 / totalTime.Value;
        else
            AverageRate = averageRate;
    }
}
