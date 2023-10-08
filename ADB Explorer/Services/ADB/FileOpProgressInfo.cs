using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public abstract class FileOpProgressInfo : ViewModelBase
{
    public string AndroidPath { get; protected set; }

    public void SetPathToCurrent()
    {
        string currentPath = ((InProgSyncProgressViewModel)Data.FileOpQ.CurrentOperation.StatusInfo).CurrentFilePath;
        if (string.IsNullOrEmpty(currentPath))
            currentPath = FileHelper.ConcatPaths(Data.FileOpQ.CurrentOperation.TargetPath.FullPath, Data.FileOpQ.CurrentOperation.FilePath.FullName);

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
}

public class HashFailInfo : FileOpErrorInfo
{
    public HashFailInfo(string androidPath, bool fileExists = true)
        : base(fileExists ? "Content mismatch" : "Missing from target location")
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
    public string WindowsPath { get; }

    public SyncErrorInfo(Match match)
        : base(match.Groups["Message"].Value)
    {
        if (match.Groups["AndroidPath"].Success)
            AndroidPath = match.Groups["AndroidPath"].Value;
        else if (match.Groups["AndroidPath1"].Success)
            AndroidPath = match.Groups["AndroidPath1"].Value;

        if (match.Groups["WindowsPath"].Success)
            WindowsPath = match.Groups["WindowsPath"].Value;
        else if (match.Groups["WindowsPath1"].Success)
            WindowsPath = match.Groups["WindowsPath1"].Value;

        if (Message.Contains(':'))
            Message = Message.Split(':').Last().Trim();
    }
}

public class AdbSyncProgressInfo : FileOpProgressInfo
{
    public int? TotalPercentage { get; }
    public int? CurrentFilePercentage { get; }
    public UInt64? CurrentFileBytesTransferred { get; }

    public AdbSyncProgressInfo(Match match)
    {
        AndroidPath = match.Groups["CurrentFile"].Value;

        if (match.Groups["TotalPercentage"].Success)
        {
            string totalPercentageRaw = match.Groups["TotalPercentage"].Value;
            TotalPercentage = totalPercentageRaw.EndsWith("%") ? int.Parse(totalPercentageRaw.TrimEnd('%')) : null;
        }

        if (match.Groups["CurrentPercentage"].Success)
        {
            string currPercentageRaw = match.Groups["CurrentPercentage"].Value;
            CurrentFilePercentage = currPercentageRaw.EndsWith("%") ? int.Parse(currPercentageRaw.TrimEnd('%')) : null;
        }

        CurrentFileBytesTransferred = match.Groups["CurrentBytes"].Success ? UInt64.Parse(match.Groups["CurrentBytes"].Value) : null;
    }

    public AdbSyncProgressInfo(string currentFile, int? totalPercentage, int? currentFilePercentage, ulong? currentFileBytesTransferred)
    {
        AndroidPath = currentFile;
        TotalPercentage = totalPercentage;
        CurrentFilePercentage = currentFilePercentage;
        CurrentFileBytesTransferred = currentFileBytesTransferred;
    }
}

public class AdbSyncStatsInfo
{
    public string SourcePath { get; }
    public UInt64 FilesTransferred { get; }
    public UInt64 FilesSkipped { get; }
    public decimal? AverageRate { get; }
    public UInt64? TotalBytes { get; }
    public decimal? TotalTime { get; }

    public AdbSyncStatsInfo(Match match)
    {
        SourcePath = match.Groups["SourcePath"].Value;
        FilesTransferred = UInt64.Parse(match.Groups["TotalTransferred"].Value);
        FilesSkipped = UInt64.Parse(match.Groups["TotalSkipped"].Value);
        AverageRate = match.Groups["AverageRate"].Success ? decimal.Parse(match.Groups["AverageRate"].Value, CultureInfo.InvariantCulture) : null;
        TotalBytes = match.Groups["TotalBytes"].Success ? UInt64.Parse(match.Groups["TotalBytes"].Value) : null;
        TotalTime = match.Groups["TotalTime"].Success ? decimal.Parse(match.Groups["TotalTime"].Value, CultureInfo.InvariantCulture) : null;
    }

    public AdbSyncStatsInfo(string targetPath, ulong filesTransferred, ulong filesSkipped, decimal? averageRate, ulong? totalBytes, decimal? totalTime)
    {
        SourcePath = targetPath;
        FilesTransferred = filesTransferred;
        FilesSkipped = filesSkipped;
        AverageRate = averageRate;
        TotalBytes = totalBytes;
        TotalTime = totalTime;
    }
}
