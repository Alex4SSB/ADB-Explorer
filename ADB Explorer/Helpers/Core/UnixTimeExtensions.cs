namespace ADB_Explorer.Helpers;

public static class UnixTimeExtensions
{
    /// <summary>
    /// Converts a DateTime (UTC or local) to Unix time (seconds since 1970-01-01 UTC).
    /// </summary>
    public static double? ToUnixTime(this DateTime? dateTime)
    {
        if (dateTime is null)
            return null;

        // Convert to UTC to ensure correct epoch alignment
        return (dateTime.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
    }

    /// <summary>
    /// Converts Unix time (seconds since 1970-01-01 UTC) to DateTime (local or UTC).
    /// </summary>
    public static DateTime? FromUnixTime(this double? unixTimeSeconds, bool asLocal = true)
    {
        if (unixTimeSeconds is null)
            return null;

        var dt = DateTime.UnixEpoch.AddSeconds(unixTimeSeconds.Value);
        return asLocal ? dt.ToLocalTime() : dt;
    }
}
