using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using System.Drawing;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models;

public class FileClass : FileStat
{
    public enum CutType
    {
        None,
        Cut,
        Copy,
        Link,
    }

    public static string CutTypeString(CutType cutType) => cutType switch
    {
        CutType.None => "",
        CutType.Cut => "Cut",
        CutType.Copy => "Copied",
        CutType.Link => "",
        _ => throw new NotImplementedException(),
    };

    private const ShellInfoManager.IconSize iconSize = ShellInfoManager.IconSize.Small;

    public FileClass(string fileName, string path, FileType type, bool isLink = false, UInt64? size = null, DateTime? modifiedTime = null, bool isTemp = false)
        : base(fileName, path, type, isLink, size)
    {
        icon = GetIcon();
        typeName = GetTypeName();
        IsTemp = isTemp;
        ModifiedTime = modifiedTime;

        SortName = new(fileName);
    }

    public FileClass(FileClass other)
        : this(other.FullName, other.FullPath, other.Type, other.IsLink, other.Size, other.ModifiedTime, other.IsTemp)
    { }

    public FileClass(FilePath other)
        : this(other.FullName, other.FullPath, other.IsDirectory ? FileType.Folder : FileType.File)
    { }

    public static FileClass GenerateAndroidFile(FileStat fileStat) => new FileClass
    (
        fileName: fileStat.FullName,
        path: fileStat.FullPath,
        type: fileStat.Type,
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        isLink: fileStat.IsLink
    );

    public static FileClass FromWindowsPath(FilePath androidTargetPath, ShellObject windowsShellObject) =>
        new(androidTargetPath)
    {
        Size = windowsShellObject.Properties.System.Size.Value,
        ModifiedTime = windowsShellObject.Properties.System.DateModified.Value
    };

    public override string ToString()
    {
        if (TrashIndex is null)
        {
            return $"{DisplayName}\n{ModifiedTimeString}\n{TypeName}\n{SizeString}";
        }
        else
        {
            return $"{DisplayName}\n{TrashIndex.OriginalPath}\n{TrashIndex.ModifiedTimeString}\n{TypeName}\n{SizeString}\n{ModifiedTimeString}";
        }
    }

    public bool IsApk => AdbExplorerConst.APK_NAMES.Contains(Extension.ToUpper());

    public bool IsInstallApk => Array.IndexOf(AdbExplorerConst.INSTALL_APK, Extension.ToUpper()) > -1;

    /// <summary>
    /// Returns the extension (including the period ".") of a regular file.<br />
    /// Returns an empty string if file has no extension, or is not a regular file.
    /// </summary>
    public override string Extension => Type is FileType.File ? base.Extension : "";

    public string ShortExtension
    {
        get
        {
            return (Extension.Length > 1 && Array.IndexOf(AdbExplorerConst.UNICODE_ICONS, char.GetUnicodeCategory(Extension[1])) > -1)
                ? Extension[1..]
                : "";
        }
    }

    

    public bool ExtensionIsGlyph { get; set; }
    public bool ExtensionIsFontIcon { get; set; }

    public string GetTypeName(string fileName)
    {
        if (IsApk)
            return "Android Application Package";

        if (string.IsNullOrEmpty(fileName) || (IsHidden && FullName.Count(c => c == '.') == 1))
            return "File";

        if (Extension.ToLower() == ".exe")
            return "Windows Executable";

        var name = ShellInfoManager.GetShellFileType(fileName);

        if (name.EndsWith("? File"))
        {
            if (ShortExtension.Length == 1)
                ExtensionIsGlyph = true;
            else
                ExtensionIsFontIcon = true;

            return $"{ShortExtension} File";
        }
        else
            return name;
    }

    private string typeName;
    public string TypeName
    {
        get => typeName;
        private set => Set(ref typeName, value);
    }

    private DateTime? modifiedTime;
    public override DateTime? ModifiedTime
    {
        get => modifiedTime;
        set
        {
            if (Set(ref modifiedTime, value))
                OnPropertyChanged(nameof(ModifiedTimeString));
        }
    }
    public string ModifiedTimeString => ModifiedTime?.ToString(CultureInfo.CurrentCulture.DateTimeFormat);
    public string SizeString => Size?.ToSize();

    private object icon;
    public object Icon
    {
        get => icon;
        private set => Set(ref icon, value);
    }

    private CutType cutState = CutType.None;
    public CutType CutState
    {
        get => cutState;
        set => Set(ref cutState, value);
    }

    public bool IsTemp { get; set; }

    private TrashIndexer trashIndex;
    public TrashIndexer TrashIndex
    {
        get => trashIndex;
        set
        {
            Set(ref trashIndex, value);
            if (value is not null)
                FullName = value.OriginalPath.Split('/')[^1];
        }
    }

    public FileNameSort SortName { get; private set; }

    public override void UpdatePath(string androidPath)
    {
        base.UpdatePath(androidPath);

        SortName = new(FullName);
    }

    private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(Path.GetTempPath(), iconSize, false));
    private static readonly BitmapSource folderLinkIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(Path.GetTempPath(), iconSize, true));
    private static readonly BitmapSource unknownFileIconBitmapSource = IconToBitmapSource(ShellInfoManager.ExtractIconByIndex("Shell32.dll", 175, iconSize));
    private static readonly BitmapSource brokenLinkIconBitmapSource = IconToBitmapSource(ShellInfoManager.ExtractIconByIndex("Shell32.dll", 271, iconSize));
    private static readonly Icon shortcutOverlayIcon = ShellInfoManager.ExtractIconByIndex("Shell32.dll", 29, iconSize);

    private static BitmapSource IconToBitmapSource(Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private object GetIcon() => Type switch
    {
        FileType.File => IsApk && !IsLink ? null : IconToBitmapSource(ExtIcon(Extension, iconSize, IsLink, IsApk)),
        FileType.Folder => IsLink ? folderLinkIconBitmapSource : folderIconBitmapSource,
        FileType.Unknown => unknownFileIconBitmapSource,
        FileType.BrokenLink => brokenLinkIconBitmapSource,
        _ => IconToBitmapSource(ExtIcon(string.Empty, iconSize, IsLink))
    };

    public void UpdateType()
    {
        TypeName = GetTypeName();
        Icon = GetIcon();
        IsRegularFile = Type is FileType.File;
    }

    private string GetTypeName()
    {
        var type = Type switch
        {
            FileType.File => GetTypeName(FullName),
            FileType.Folder => "Folder",
            FileType.Unknown => "",
            _ => GetFileTypeName(Type),
        };

        if (IsLink && Type is not FileType.BrokenLink)
            type = string.IsNullOrEmpty(type) ? "Link" : $"{type} (Link)";

        return type;
    }

    private static Icon ExtIcon(string extension, ShellInfoManager.IconSize iconSize, bool isLink, bool isApk = false)
    {
        // No extension -> "*" which means unknown file 
        if (string.IsNullOrEmpty(extension))
        {
            extension = "*";
        }

        Icon icon;
        var iconId = new Tuple<string, bool>(extension, isLink);
        if (!FileIcons.TryGetValue(iconId, out Icon value))
        {
            if (isApk)
            {
                icon = isLink ? shortcutOverlayIcon : null;
            }
            else
                icon = ShellInfoManager.GetExtensionIcon(extension, iconSize, isLink);

            FileIcons.Add(iconId, icon);
        }
        else
            icon = value;

        return icon;
    }

    public static ulong TotalSize(IEnumerable<FileClass> files)
    {
        if (files.Any(f => f.Type is not FileType.File || f.IsLink))
            return 0;
        
        return (ulong)files.Select(f => (decimal)f.Size.GetValueOrDefault(0)).Sum();
    }

    public static string ExistingIndexes(ObservableList<FileClass> fileList, string namePrefix, CutType cutType = CutType.None)
    {
        var existingItems = fileList.Where(item => item.NoExtName.StartsWith(namePrefix));
        var suffixes = existingItems.Select(item => item.NoExtName[namePrefix.Length..].Trim());

        if (cutType is CutType.Copy or CutType.Link)
        {
            suffixes = suffixes.Select(item => item.Replace("- Copy", "").Trim());
        }

        return ExistingIndexes(suffixes, cutType);
    }

    public static string ExistingIndexes(IEnumerable<string> suffixes, CutType cutType = CutType.None)
    {
        var copySuffix = cutType is CutType.Copy or CutType.Link ? " - Copy" : "";

        var indexes = (from i in suffixes
                       where int.TryParse(i, out _)
                       select int.Parse(i)).ToList();
        if (suffixes.Any(s => s == ""))
            indexes.Add(0);

        indexes.Sort();
        if (indexes.Count == 0 || indexes[0] != 0)
            return "";

        var result = "";
        for (int i = 0; i < indexes.Count; i++)
        {
            if (indexes[i] > i)
            {
                result = $"{copySuffix} {i}";
                break;
            }
        }
        if (result == "")
            result = $"{copySuffix} {indexes.Count}";

        return result;
    }

    protected override bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (propertyName is nameof(FullName) or nameof(Type) or nameof(IsLink))
        {
            UpdateType();
        }

        return base.Set(ref storage, value, propertyName);
    }

    public static explicit operator SyncFile(FileClass self)
        => new(self.FullPath, self.Type);
}

public class FileNameSort : IComparable
{
    public string Name { get; }

    public FileNameSort(string name)
    {
        Name = name;
    }

    public override string ToString()
    {
        return Name;
    }

    public int CompareTo(object obj)
    {
        if (obj is not FileNameSort other)
            return 0;

        return ShellInfoManager.StringCompareLogical(Name, other.Name);
    }
}
