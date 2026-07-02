using ADB_Explorer.Helpers;

namespace ADB_Explorer.Models;

[Flags]
public enum AccessMask
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    All = Read | Write | Execute,
}

public readonly record struct LocationInfo(
    string? User,
    string? Group,
    int? OwnerUid,
    int? OwnerGid,
    UnixFileMode? Permissions,
    AccessMask ProbedAccess,
    DateTimeOffset? AccessTime,
    DateTimeOffset? CreationTime,
    DateTimeOffset? ModifiedTime);

public static partial class ShellAccessHelper
{
    public const string AccessMarker = "ADB_ACCESS:";

    [GeneratedRegex(@"^uid=(\d+)\([^)]*\)\s+gid=(\d+)\([^)]*\)(?:\s+groups=(.+))?$")]
    private static partial Regex IdLineRegex();

    [GeneratedRegex(@"(\d+)\([^)]+\)")]
    private static partial Regex GroupsRE();

    public static ShellIdentity? ParseShellIdentity(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        string? userName = null;
        string? idLine = null;

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (idLine is null && line.StartsWith("uid=", StringComparison.Ordinal))
            {
                idLine = line;
                continue;
            }

            if (userName is null && !line.StartsWith("uid=", StringComparison.Ordinal))
                userName = line;
        }

        if (idLine is null)
            return null;

        var match = IdLineRegex().Match(idLine);
        if (!match.Success)
            return null;

        var uid = int.Parse(match.Groups[1].Value);
        var gid = int.Parse(match.Groups[2].Value);
        userName ??= uid == 0 ? "root" : "shell";

        var groups = new HashSet<int> { gid };
        if (match.Groups[3].Success)
        {
            var matches = GroupsRE().Matches(match.Groups[3].Value);

            matches.Where(m => match.Success)
                   .Select(m => int.Parse(m.Groups[1].Value))
                   .ForEach(m => groups.Add(m));
        }

        return new ShellIdentity(userName, uid, gid, groups);
    }

    public static LocationInfo? ParseLocationInfo(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        string? statLine = null;
        AccessMask probed = AccessMask.None;

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith(AccessMarker, StringComparison.Ordinal))
            {
                probed = ParseProbedAccess(line[AccessMarker.Length..]);
                continue;
            }

            if (statLine is null && !line.StartsWith(AccessMarker, StringComparison.Ordinal))
                statLine = line;
        }

        if (statLine is null)
            return new LocationInfo() { ProbedAccess = probed };

        var parts = statLine.Split(AdbExplorerConst.ADB_FIELD_SEP);
        if (parts.Length < 8)
            return new LocationInfo() { ProbedAccess = probed };

        try
        {
            var user = parts[0];
            var group = parts[1];
            var ownerUid = int.Parse(parts[2]);
            var ownerGid = int.Parse(parts[3]);
            var permissions = (UnixFileMode)Convert.ToInt32(parts[4], 8);
            var accessTime = DateTimeOffset.Parse(parts[5].Trim(), CultureInfo.InvariantCulture);
            var creationTime = DateTimeOffset.Parse(parts[6].Trim(), CultureInfo.InvariantCulture);
            var modifiedTime = DateTimeOffset.Parse(parts[7].Trim(), CultureInfo.InvariantCulture);

            return new LocationInfo(user, group, ownerUid, ownerGid, permissions, probed, accessTime, creationTime, modifiedTime);
        }
        catch
        {
            return new LocationInfo() { ProbedAccess = probed };
        }
    }

    public static AccessMask ParseProbedAccess(string value)
    {
        if (value.Length != 3)
            return AccessMask.None;

        var mask = AccessMask.None;
        if (value[0] == '1')
            mask |= AccessMask.Read;
        if (value[1] == '1')
            mask |= AccessMask.Write;
        if (value[2] == '1')
            mask |= AccessMask.Execute;

        return mask;
    }

    public static AccessMask FromUnixTriplet(int triplet)
    {
        var mask = AccessMask.None;
        if ((triplet & 4) != 0)
            mask |= AccessMask.Read;
        if ((triplet & 2) != 0)
            mask |= AccessMask.Write;
        if ((triplet & 1) != 0)
            mask |= AccessMask.Execute;

        return mask;
    }

    public static AccessMask ResolveEffective(UnixFileMode mode, int ownerUid, int ownerGid, ShellIdentity identity)
    {
        if (identity.IsRoot)
            return AccessMask.All;

        int triplet;
        var modeBits = (int)mode & 0xFFF;

        if (identity.Uid == ownerUid)
            triplet = (modeBits >> 6) & 7;
        else if (identity.Gid == ownerGid || identity.Groups.Contains(ownerGid))
            triplet = (modeBits >> 3) & 7;
        else
            triplet = modeBits & 7;

        return FromUnixTriplet(triplet);
    }

    public static AccessMask ApplyRestrictions(AccessMask access, DriveRestrictions restrictions)
    {
        if (restrictions.ReadOnly)
            access &= ~AccessMask.Write;

        if (restrictions.NoExec)
            access &= ~AccessMask.Execute;

        return access;
    }

    /// <summary>
    /// MediaProvider FUSE blocks rename/delete of the volume-root <c>Android</c> directory entry
    /// (see FuseDaemon.cpp: <c>GetEffectiveRootPath() + "/android"</c>), while writes inside it remain allowed.
    /// </summary>
    public static bool IsFuseProtectedAndroidRoot(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return AdbExplorerConst.FUSE_PROTECTED_ANDROID_ROOT_PATHS.Contains(path.TrimEnd('/'));
    }

    public static AccessMask ResolveLocationAccess(
        string path,
        LocationInfo? info,
        ShellIdentity? identity,
        DriveRestrictions restrictions)
    {
        AccessMask access;

        if (info?.ProbedAccess is AccessMask probed && probed != AccessMask.None)
        {
            access = probed;
        }
        else if (info is { } location
                 && location.Permissions is UnixFileMode mode
                 && location.OwnerUid is int ownerUid
                 && location.OwnerGid is int ownerGid
                 && identity is not null)
        {
            access = ResolveEffective(mode, ownerUid, ownerGid, identity);
        }
        else if (identity?.IsRoot == true)
        {
            access = AccessMask.All;
        }
        else
        {
            access = AccessMask.Read | AccessMask.Execute;
        }

        return ApplyRestrictions(access, restrictions);
    }
}
