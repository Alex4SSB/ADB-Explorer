namespace ADB_Explorer.Services;

public abstract class FileOpProgressInfo
{

}

public class SyncErrorInfo : FileOpProgressInfo
{
    public string Message { get; }

    public SyncErrorInfo(Match match)
    {
        Message = match.Groups["Message"].Value;
    }
}

public class AdbSyncProgressInfo : FileOpProgressInfo
{
    public string CurrentFile { get; }
    public int? TotalPercentage { get; }
    public int? CurrentFilePercentage { get; }
    public UInt64? CurrentFileBytesTransferred { get; }

    public AdbSyncProgressInfo(Match match)
    {
        CurrentFile = match.Groups["CurrentFile"].Value;

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
        CurrentFile = currentFile;
        TotalPercentage = totalPercentage;
        CurrentFilePercentage = currentFilePercentage;
        CurrentFileBytesTransferred = currentFileBytesTransferred;
    }
}

public class AdbSyncStatsInfo
{
    public string TargetPath { get; }
    public UInt64 FilesTransferred { get; }
    public UInt64 FilesSkipped { get; }
    public decimal? AverageRate { get; }
    public UInt64? TotalBytes { get; }
    public decimal? TotalTime { get; }

    public AdbSyncStatsInfo(Match match)
    {
        TargetPath = match.Groups["TargetPath"].Value;
        FilesTransferred = UInt64.Parse(match.Groups["TotalTransferred"].Value);
        FilesSkipped = UInt64.Parse(match.Groups["TotalSkipped"].Value);
        AverageRate = match.Groups["AverageRate"].Success ? decimal.Parse(match.Groups["AverageRate"].Value, CultureInfo.InvariantCulture) : null;
        TotalBytes = match.Groups["TotalBytes"].Success ? UInt64.Parse(match.Groups["TotalBytes"].Value) : null;
        TotalTime = match.Groups["TotalTime"].Success ? decimal.Parse(match.Groups["TotalTime"].Value, CultureInfo.InvariantCulture) : null;
    }

    public AdbSyncStatsInfo(string targetPath, ulong filesTransferred, ulong filesSkipped, decimal? averageRate, ulong? totalBytes, decimal? totalTime)
    {
        TargetPath = targetPath;
        FilesTransferred = filesTransferred;
        FilesSkipped = filesSkipped;
        AverageRate = averageRate;
        TotalBytes = totalBytes;
        TotalTime = totalTime;
    }
}
