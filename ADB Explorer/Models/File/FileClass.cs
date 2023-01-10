using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using System.Drawing;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models;

public class FileClass : FileStat
{
    public enum CutType
    {
        None,
        Cut,
        Copy,
    }

    public static string CutTypeString(CutType cutType) => cutType switch
    {
        CutType.None => "",
        CutType.Cut => "Cut",
        CutType.Copy => "Copied",
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

    public static FileClass GenerateAndroidFile(FileStat fileStat) => new FileClass
    (
        fileName: fileStat.FullName,
        path: fileStat.FullPath,
        type: fileStat.Type,
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        isLink: fileStat.IsLink
    );

    public static FileClass FromWindowsPath(FilePath androidTargetPath, FilePath windowsFilePath)
    {
        bool isDir = false;
        ulong? fileSize = null;
        DateTime? modifiedTime = null;
        try
        {
            isDir = Directory.Exists(windowsFilePath.FullPath);
            if (!isDir)
            {
                var fileInfo = new FileInfo(windowsFilePath.FullPath);
                fileSize = (ulong)fileInfo.Length;
                modifiedTime = fileInfo.LastWriteTime;
            }
            
        }
        catch (Exception) {}

        return new FileClass
        (
            fileName: windowsFilePath.FullName,
            path: androidTargetPath.FullPath + '/' + windowsFilePath.FullName,
            type: isDir ? FileType.Folder : FileType.File,
            size: fileSize,
            modifiedTime: modifiedTime
        );
    }

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

    private bool? isApk = null;
    public bool IsApk
    {
        get
        {
            if (isApk is null)
            {
                isApk = Array.IndexOf(AdbExplorerConst.APK_NAMES, Extension.ToUpper()) > -1;
            }

            return (bool)isApk;
        }
    }

    public bool IsInstallApk => Array.IndexOf(AdbExplorerConst.INSTALL_APK, Extension.ToUpper()) > -1;

    public bool IsHidden => FullName.StartsWith('.');

    public string Extension
    {
        get
        {
            if (Type is not FileType.File || (IsHidden && FullName.Count(c => c == '.') == 1))
                return "";

            return Path.GetExtension(FullName);
        }
    }

    public string ShortExtension { get { return (Extension.Length > 1 && Array.IndexOf(AdbExplorerConst.UNICODE_ICONS, char.GetUnicodeCategory(Extension[1])) > -1) ? Extension[1..] : ""; } }

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

    private static BitmapSource IconToBitmapSource(Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private object GetIcon()
    {
        return Type switch
        {
            FileType.File => IconToBitmapSource(ExtIcon(Extension, iconSize, IsLink, IsApk)),
            FileType.Folder => IsLink ? folderLinkIconBitmapSource : folderIconBitmapSource,
            FileType.Unknown => unknownFileIconBitmapSource,
            _ => IconToBitmapSource(ExtIcon(string.Empty, iconSize, IsLink))
        };
    }

    public void UpdateType()
    {
        TypeName = GetTypeName();
        Icon = GetIcon();
    }

    private string GetTypeName()
    {
        return Type switch
        {
            FileType.File => IsLink ? "Link" : GetTypeName(FullName),
            FileType.Folder => IsLink ? "Link" : "Folder",
            FileType.Unknown => "",
            _ => Type.Name(),
        };
    }

    private static Icon ExtIcon(string extension, ShellInfoManager.IconSize iconSize, bool isLink, bool isApk = false)
    {
        // No extension -> "*" which means unknown file 
        if (extension == string.Empty)
        {
            extension = "*";
        }

        Icon icon;
        var iconId = new Tuple<string, bool>(extension, isLink);
        if (!FileIcons.ContainsKey(iconId))
        {
            if (isApk)
            {
                icon = Properties.Resources.APK_icon;
            }
            else
                icon = ShellInfoManager.GetExtensionIcon(extension, iconSize, isLink);

            FileIcons.Add(iconId, icon);
        }
        else
        {
            icon = FileIcons[iconId];
        }

        return icon;
    }

    public static ulong TotalSize(IEnumerable<FileClass> files)
    {
        if (files.Any(i => i.Type != FileType.File))
            return 0;
        
        return (ulong)files.Select(f => (decimal)f.Size.GetValueOrDefault(0)).Sum();
    }

    public static string ExistingIndexes(ObservableList<FileClass> fileList, string namePrefix, bool isCopy = false)
    {
        var existingItems = fileList.Where(item => item.NoExtName.StartsWith(namePrefix));
        var suffixes = existingItems.Select(item => item.NoExtName[namePrefix.Length..].Trim());

        if (isCopy)
        {
            suffixes = suffixes.Select(item => item.Replace("- Copy", "").Trim());
        }

        return ExistingIndexes(suffixes, isCopy);
    }

    public static string ExistingIndexes(IEnumerable<string> suffixes, bool isCopy = false)
    {
        var copySuffix = isCopy ? " - Copy" : "";

        var indexes = (from i in suffixes
                       where int.TryParse(i, out _)
                       select int.Parse(i)).ToList();
        if (suffixes.Any(s => s == ""))
            indexes.Add(0);

        indexes.Sort();
        if (!indexes.Any() || indexes[0] != 0)
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
        if (propertyName == "FileName" || propertyName == "Type" || propertyName == "IsLink")
        {
            Icon = GetIcon();
            TypeName = GetTypeName();
        }

        return base.Set(ref storage, value, propertyName);
    }
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
