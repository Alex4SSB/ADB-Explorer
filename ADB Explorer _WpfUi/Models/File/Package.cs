using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public class Package : ViewModelBase, IBrowserItem
{
    public enum PackageType
    {
        System,
        User,
    }
    
    private string name;
    public string Name
    {
        get => name;
        private set => Set(ref name, value);
    }

    private string path;
    public string Path
    {
        get => path;
        private set => Set(ref path, value);
    }

    public string DisplayName => Name;

    private PackageType type;
    public PackageType Type
    {
        get => type;
        private set => Set(ref type, value);
    }

    private long? uid = null;
    public long? Uid
    {
        get => uid;
        private set => Set(ref uid, value);
    }

    private long? version = null;
    public long? Version
    {
        get => version;
        private set => Set(ref version, value);
    }

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
