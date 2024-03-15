using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Drawing;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

internal class IconHelper
{
    private const ShellInfoManager.IconSize iconSize = ShellInfoManager.IconSize.Small;
    private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(Path.GetTempPath(), iconSize, false));
    private static readonly BitmapSource folderLinkIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(Path.GetTempPath(), iconSize, true));
    private static readonly BitmapSource unknownFileIconBitmapSource = IconToBitmapSource(ShellInfoManager.ExtractIconByIndex("Shell32.dll", 175, iconSize));
    private static readonly BitmapSource brokenLinkIconBitmapSource = IconToBitmapSource(ShellInfoManager.ExtractIconByIndex("Shell32.dll", 271, iconSize));
    private static readonly Icon shortcutOverlayIcon = ShellInfoManager.ExtractIconByIndex("Shell32.dll", 29, iconSize);

    public static BitmapSource IconToBitmapSource(Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
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
        if (!Data.FileIcons.TryGetValue(iconId, out Icon value))
        {
            if (isApk)
            {
                icon = isLink ? shortcutOverlayIcon : null;
            }
            else
                icon = ShellInfoManager.GetExtensionIcon(extension, iconSize, isLink);

            Data.FileIcons.Add(iconId, icon);
        }
        else
            icon = value;

        return icon;
    }

    public static object GetIcon(FileClass file) => file.Type switch
    {
        FileType.File => file.IsApk && !file.IsLink ? null : IconToBitmapSource(ExtIcon(file.Extension, iconSize, file.IsLink, file.IsApk)),
        FileType.Folder => file.IsLink ? folderLinkIconBitmapSource : folderIconBitmapSource,
        FileType.Unknown => unknownFileIconBitmapSource,
        FileType.BrokenLink => brokenLinkIconBitmapSource,
        _ => IconToBitmapSource(ExtIcon(string.Empty, iconSize, file.IsLink))
    };
}
