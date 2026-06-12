namespace ADB_Explorer.Models;

public readonly record struct DriveRestrictions(
    bool NoExec,
    bool ReadOnly,
    bool NoAccessTime)
{
    public static readonly DriveRestrictions None = default;

    public bool HasAny => NoExec || ReadOnly;

    public bool SupportsAccessTime => !NoAccessTime;

    public string IconGlyph => ReadOnly ? "\uE72E" : "\uE7BA";

    public bool NoSymbolicLinks => NoExec;
    public bool RestrictedNaming => NoExec;
    public bool CaseInsensitiveNames => NoExec;
    public bool NoApkInstall => NoExec;

    public static DriveRestrictions From(string[]? mountOptions)
    {
        if (mountOptions is null || mountOptions.Length == 0)
            return None;

        var options = mountOptions
            .Select(o => o.Trim().ToLowerInvariant())
            .Where(o => o.Length > 0)
            .ToHashSet();

        return new(
            NoExec: HasOption(options, "noexec"),
            ReadOnly: HasOption(options, "ro"),
            NoAccessTime: HasOption(options, "noatime"));
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
