namespace ADB_Explorer.Models;

public partial class Package : ObservableObject, IBrowserItem
{
    public enum PackageType
    {
        System,
        User,
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _path;

    public string DisplayName => Name;

    [ObservableProperty]
    private PackageType _type;

    [ObservableProperty]
    private long? _uid = null;

    [ObservableProperty]
    private long? _version = null;

    [ObservableProperty]
    private bool isSelected;

    public static Package New(string package, PackageType type)
    {
        var match = AdbRegEx.RE_PACKAGE_LISTING().Match(package);
        if (!match.Success)
            return null;

        return new Package(match.Groups["Name"].Value, type, match.Groups["Uid"].Value, match.Groups["Version"].Value, match.Groups["Path"].Value);
    }

    public Package(string name, PackageType type, string uid, string version, string path)
    {
        Name = name;
        Type = type;
        Path = path;

        if (long.TryParse(uid, out long resU))
            Uid = resU;

        if (long.TryParse(version, out long resV))
            Version = resV;
    }

    public override string ToString()
    {
        return $"{Name}\n{Type}\n{Uid}\n{Version}\n{Path}";
    }
}
