using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

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

    public string ModifiedTimeString => TabularDateFormatter.Format(_file.ModifiedTime, Thread.CurrentThread.CurrentCulture);

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

    [ObservableProperty]
    private bool _isDragOver;

    protected FileViewModelBase(FileClass file)
    {
        _file = file;
        _typeName = GetTypeName();
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

    public void OnModifiedTimeChanged()
    {
        OnPropertyChanged(nameof(ModifiedTimeString));
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
