namespace ADB_Explorer.Models;

public readonly record struct DriveRestrictions(
    bool NoExec,
    bool ReadOnly,
    bool NoAccessTime,
    bool FuseFs = false)
{
    public static readonly DriveRestrictions None = default;

    public bool HasAny => NoExec || ReadOnly;

    public bool SupportsAccessTime => !NoAccessTime;

    public string IconGlyph => ReadOnly ? "\uE72E" : "\uE7BA";

    public bool NoSymbolicLinks => FuseFs;
    public bool RestrictedNaming => FuseFs;
    public bool CaseInsensitiveNames => FuseFs;
    public bool NoApkInstall => NoExec;

    public static DriveRestrictions From(string[]? mountOptions, string? fileSystemType = null)
    {
        if (mountOptions is null || mountOptions.Length == 0)
            return None;

        var options = mountOptions
            .Select(o => o.Trim().ToLowerInvariant())
            .Where(o => o.Length > 0)
            .ToHashSet();

        var noExec = HasOption(options, "noexec");

        return new(
            NoExec: noExec,
            ReadOnly: HasOption(options, "ro"),
            NoAccessTime: HasOption(options, "noatime"),
            // naming/symlink limits come from FUSE/FAT, not noexec; old callers without an fs type keep the noexec guess
            FuseFs: fileSystemType is null ? noExec : IsFuseLikeFs(fileSystemType));
    }

    private static bool IsFuseLikeFs(string fileSystemType)
    {
        var type = fileSystemType.ToLowerInvariant();

        return type.Contains("fuse")
            || type.Contains("sdcardfs")
            || type.Contains("esdfs")
            || type.Contains("fat")     // vfat / exfat / fat32
            || type.Contains("msdos");
    }

    private static bool HasOption(HashSet<string> options, string name) =>
        options.Contains(name) || options.Any(o => o.StartsWith(name + "="));

    public string GetTooltipText()
    {
        if (!HasAny)
            return "";

        var lines = new List<string> { Strings.Resources.S_DRIVE_RESTRICTIONS_HEADER };

        if (ReadOnly)
            lines.Add(Strings.Resources.S_RESTRICTION_READ_ONLY);
        if (NoExec)
            lines.Add(Strings.Resources.S_RESTRICTION_NOEXEC);

        return string.Join(Environment.NewLine, lines);
    }
}
