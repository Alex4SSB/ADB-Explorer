using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.ViewModels;

public partial class FileViewModelBase : ObservableObject
{
    protected readonly FileClass _file;

    private string _typeName;
    public string TypeName
    {
        get => _typeName;
        protected set
        {
            if (SetProperty(ref _typeName, value))
            {
                OnPropertyChanged(nameof(TypeIsRtl));
                OnPropertyChanged(nameof(TypeFlowDirection));
            }
        }
    }

    public bool TypeIsRtl => TextHelper.ContainsRtl(TypeName);
    public FlowDirection TypeFlowDirection => TypeIsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string ModifiedTimeString => TabularDateFormatter.Format(_file.ModifiedTime, Data.Settings.ActualUICulture);
    public string ModifiedTimeWithOffsetString => _file.ModifiedTimeWithOffset is { } dto
        ? TabularDateFormatter.Format(dto, Data.Settings.ActualUICulture)
        : TabularDateFormatter.Format(_file.ModifiedTime, Data.Settings.ActualUICulture);
    public string CreationTimeString => TabularDateFormatter.Format(_file.CreationTime, Data.Settings.ActualUICulture);
    public string LastAccessTimeString => TabularDateFormatter.Format(_file.LastAccessTime, Data.Settings.ActualUICulture);

    public string SizeString => _file.IsDirectory ? "" : _file.Size?.BytesToSize(true);

    public string ShortExtension
    {
        get
        {
            return (_file.Extension.Length > 1 && Array.IndexOf(AdbExplorerConst.UNICODE_ICONS, char.GetUnicodeCategory(_file.Extension[1])) > -1)
                ? _file.Extension[1..]
                : "";
        }
    }

    public string UserPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.UserRead,
                                        UnixFileMode.UserWrite,
                                        UnixFileMode.UserExecute);
        }
    }

    public string GroupPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.GroupRead,
                                        UnixFileMode.GroupWrite,
                                        UnixFileMode.GroupExecute);
        }
    }

    public string OtherPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.OtherRead,
                                        UnixFileMode.OtherWrite,
                                        UnixFileMode.OtherExecute);
        }
    }

    [ObservableProperty]
    public partial bool IsDragOver { get; set; }

    [ObservableProperty]
    public partial bool IsInEditMode { get; set; }

    [ObservableProperty]
    public partial bool IsRenameUnixLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameFuseLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameWindowsLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameDriveRootLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameUnique { get; set; }

    protected FileViewModelBase(FileClass file)
    {
        _file = file;
        _typeName = GetTypeName();
    }

    public static void RenameTextChanged(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        textBox.FilterString(Data.CurrentDrive.IsFUSE
            ? AdbExplorerConst.INVALID_NTFS_CHARS
            : AdbExplorerConst.INVALID_UNIX_CHARS);

        var vm = file.ActiveViewModel;

        vm.IsRenameUnixLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Unix);
        vm.IsRenameFuseLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.FUSE);
        vm.IsRenameWindowsLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Windows);
        vm.IsRenameDriveRootLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.WinRoot);

        var fullName = Data.Settings.ShowExtensions
            ? textBox.Text
            : textBox.Text + file.Extension;

        var comparison = Data.CurrentDrive.IsFUSE
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        vm.IsRenameUnique = !Data.DirList.FileList.Except([file]).Any(f => f.FullName.Equals(fullName, comparison));
    }

    public static void RenameKeyDown(TextBox textBox, Key key, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        if (key is Key.Escape or Key.F2)
        {
            var name = FileHelper.DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
                Data.DirList.FileList.Remove(file);
            else
                textBox.Text = name;

            exitEditMode(file);
        }
        else if (key is Key.Enter)
        {
            exitEditMode(file);
        }
    }

    public static void RenameCommit(TextBox textBox, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        FileActionLogic.Rename(textBox);
        exitEditMode(file);
    }

    public virtual void UpdateType()
    {
        TypeName = GetTypeName();
    }

    internal string GetTypeName()
    {
        var type = _file.Type switch
        {
            AbstractFile.FileType.File => GetTypeName(_file.FullName),
            AbstractFile.FileType.Folder => Strings.Resources.S_MENU_FOLDER,
            AbstractFile.FileType.Unknown => "",
            _ => AbstractFile.GetFileTypeName(_file.Type),
        };

        if (_file.IsLink && _file.Type is not AbstractFile.FileType.BrokenLink)
            type = string.IsNullOrEmpty(type)
                ? Strings.Resources.S_FILE_TYPE_LINK
                : string.Format(Strings.Resources.S_KNOWN_TYPE_LINK, type);

        return type;
    }

    private string GetTypeName(string fileName)
    {
        if (_file.IsApk)
            return Strings.Resources.S_FILE_TYPE_APK;

        if (string.IsNullOrEmpty(fileName) || (_file.IsHidden && _file.FullName.Count(c => c == '.') == 1))
            return Strings.Resources.S_MENU_FILE;

        if (_file.Extension.Equals(".exe", StringComparison.CurrentCultureIgnoreCase))
            return Strings.Resources.S_FILE_TYPE_EXE;

        if (!Ascii.IsValid(_file.Extension))
        {
            if (ShortExtension.Length is 0)
                return $"{_file.Extension[1..]} {Strings.Resources.S_MENU_FILE}";

            return $"{ShortExtension} {Strings.Resources.S_MENU_FILE}";
        }
        else
        {
            return NativeMethods.GetShellFileType(fileName);
        }
    }

    private static string GetPermissionsString(UnixFileMode fileMode, UnixFileMode read, UnixFileMode write, UnixFileMode execute)
    {
        var userPermissions = fileMode & (read | write | execute);
        string result = "";

        if (userPermissions.HasFlag(read))
            result += "r";
        else
            result += "-";

        if (userPermissions.HasFlag(write))
            result += "w";
        else
            result += "-";

        if (userPermissions.HasFlag(execute))
            result += "x";
        else
            result += "-";

        return result;
    }

    public void OnModifiedTimeChanged()
    {
        OnPropertyChanged(nameof(ModifiedTimeString));
        OnPropertyChanged(nameof(ModifiedTimeWithOffsetString));
    }

    public void OnSizeChanged()
    {
        OnPropertyChanged(nameof(SizeString));
    }

    public virtual void Dispose()
    {
        TypeName = null;
    }
}
