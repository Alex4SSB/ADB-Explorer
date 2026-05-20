using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public partial class FolderViewModel(FileClass file) : FileViewModelBase(file)
{
    [ObservableProperty]
    public partial bool ExtensionIsGlyph { get; set; }

    [ObservableProperty]
    public partial bool ExtensionIsFontIcon { get; set; }

    public override void UpdateType()
    {
        base.UpdateType();
        UpdateExtensionDisplay();
    }

    private void UpdateExtensionDisplay()
    {
        if (_file.Type is not AbstractFile.FileType.File
            || _file.IsApk
            || string.IsNullOrEmpty(_file.FullName)
            || (_file.IsHidden && _file.FullName.Count(c => c == '.') == 1)
            || _file.Extension.Equals(".exe", StringComparison.CurrentCultureIgnoreCase))
        {
            ExtensionIsGlyph = false;
            ExtensionIsFontIcon = false;
            return;
        }

        if (!Ascii.IsValid(_file.Extension))
        {
            if (ShortExtension.Length == 1)
            {
                ExtensionIsGlyph = true;
                ExtensionIsFontIcon = false;
            }
            else if (ShortExtension.Length > 1)
            {
                ExtensionIsGlyph = false;
                ExtensionIsFontIcon = true;
            }
            else
            {
                ExtensionIsGlyph = false;
                ExtensionIsFontIcon = false;
            }
        }
        else
        {
            ExtensionIsGlyph = false;
            ExtensionIsFontIcon = false;
        }
    }

    public override void Dispose()
    {
        ExtensionIsGlyph = false;
        ExtensionIsFontIcon = false;

        base.Dispose();
    }
}
