using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public partial class Package : ObservableObject, IBrowserItem
{
    public enum PackageType
    {
        System,
        User,
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Path { get; set; }

    public string DisplayName => Name;

    public FolderViewModel FolderViewModel => null;

    [ObservableProperty]
    public partial PackageType Type { get; set; }

    [ObservableProperty]
    public partial long? Uid { get; set; } = null;

    [ObservableProperty]
    public partial long? Version { get; set; } = null;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

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
