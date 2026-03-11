// Part of FileToIcon from Code Project article by Leung Yat Chun
// https://www.codeproject.com/Articles/32059/WPF-Filename-To-Icon-Converter
// Used and modified under the LGPLv3 license

using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Drawing;
using static Services.NativeMethods;

namespace ADB_Explorer.Helpers;

public class FileToIconConverter
{
    public enum IconSize : uint
    {
        Large,
        Small,
        ExtraLarge,
        Jumbo,
        Thumbnail,
    }

    private readonly record struct SpecialIcon(string DllPath, int Index)
    {
        public static readonly SpecialIcon None = new(null, -1);
        public bool IsValid => Index >= 0;
    }

    private const string Shell32 = "shell32.dll";
    private const string Imageres = "imageres.dll";
    private static readonly SpecialIcon FolderIcon = new(Shell32, 3);
    private static readonly SpecialIcon LinkOverlayIcon = new(Shell32, 29);
    private static readonly SpecialIcon UnknownIcon = new(Shell32, 224);
    private static readonly SpecialIcon BrokenLinkIcon = new(Shell32, 271);
    private static readonly SpecialIcon GalleryIcon = new(Shell32, 318);
    private static readonly SpecialIcon MusicFolderIcon = new(Imageres, 103);
    private static readonly SpecialIcon DocumentsFolderIcon = new(Imageres, 107);
    private static readonly SpecialIcon PicturesFolderIcon = new(Imageres, 108);
    private static readonly SpecialIcon DownloadsFolderIcon = new(Imageres, 175);
    private static readonly SpecialIcon VideosFolderIcon = new(Imageres, 178);

    private static readonly Dictionary<string, object> iconDic = [];
    private static readonly SysImageList _imgList = new(SysImageListSize.SHIL_JUMBO);

    // <summary>
    /// Return large file icon of the specified file.
    /// </summary>
    private static Icon GetFileIcon(string fileName, IconSize size)
    {
        var flags = NativeMethods.FileInfoFlags.SHGFI_SYSICONINDEX;
        
        if (!fileName.Contains(':'))
            flags |= NativeMethods.FileInfoFlags.SHGFI_USEFILEATTRIBUTES;

        if (size == IconSize.Small)
            flags |= NativeMethods.FileInfoFlags.SHGFI_SMALLICON;
        
        return NativeMethods.GetIcon(fileName, flags);
    }
    private static Icon GetIconFromIndex(SpecialIcon specialIcon, IconSize size)
        => NativeMethods.ExtractIconByIndex(specialIcon.DllPath, specialIcon.Index, size);

    private static Bitmap ResizeImage(Bitmap imgToResize, System.Drawing.Size size, int spacing, bool addBorder = true)
    {
        int destWidth = imgToResize.Width;
        int destHeight = imgToResize.Height;

        int leftOffset = (size.Width - destWidth) / 2;
        int topOffset = (size.Height - destHeight) / 2;

        Bitmap b = new(size.Width, size.Height);
        Graphics g = Graphics.FromImage(b);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;

        var Gray222 = System.Drawing.Color.FromArgb(222, 222, 222);
        var Gray225 = System.Drawing.Color.FromArgb(225, 225, 225);
        var Gray232 = System.Drawing.Color.FromArgb(232, 232, 232);
        var Gray244 = System.Drawing.Color.FromArgb(244, 244, 244);

        if (addBorder)
        {
            g.DrawRectangle(new System.Drawing.Pen(Gray232),
                spacing + 1,
                spacing + 1,
                size.Width - (spacing + 1) * 2 - 1,
                size.Height - (spacing + 1) * 2 - 1);

            g.DrawRectangle(new System.Drawing.Pen(Gray222),
                spacing,
                spacing,
                size.Width - spacing * 2 - 1,
                size.Height - spacing * 2 - 1);
        }

        g.DrawImage(imgToResize, leftOffset, topOffset, destWidth, destHeight);
        g.Dispose();

        if (addBorder)
        {
            b.SetPixel(spacing, spacing, Gray244);
            b.SetPixel(spacing, size.Height - 1, Gray244);
            b.SetPixel(size.Width - 1, spacing, Gray244);
            b.SetPixel(size.Width - 1, size.Height - 1, Gray244);

            b.SetPixel(spacing + 1, spacing + 1, Gray225);
            b.SetPixel(spacing + 1, size.Height - 2, Gray225);
            b.SetPixel(size.Width - 2, spacing + 1, Gray225);
            b.SetPixel(size.Width - 2, size.Height - 2, Gray225);
        } 

        return b;
    }

    private static BitmapSource LoadBitmap(Bitmap source)
    {
        var hBitmap = source.GetHbitmap();
        //Memory Leak fixes, for more info : http://social.msdn.microsoft.com/forums/en-US/wpf/thread/edcf2482-b931-4939-9415-15b3515ddac6/
        try
        {
            var bmpSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmpSource.Freeze();
            return bmpSource;
        }
        finally
        {
            NativeMethods.MDeleteObject(hBitmap);
        }
    }

    private static string ReturnKey(string fileName, IconSize size, AbstractFile.SpecialFileType specialType, bool bitmapSource = true)
    {
        string key;

        if (specialType.HasFlag(AbstractFile.SpecialFileType.Regular))
            key = Path.GetExtension(fileName).ToLower();
        else
        {
            var specialIcon = SpecialTypeIndex(specialType, fileName);
            key = specialIcon.IsValid
                ? $"#{specialIcon.DllPath}_{specialIcon.Index}#"
                : $"#{Enum.GetName(specialType).ToUpper()}#";
        }

        var sizeKey = size switch
        {
            IconSize.Jumbo or IconSize.Thumbnail => "J",
            IconSize.ExtraLarge => "XL",
            IconSize.Large => "L",
            IconSize.Small => "S",
            _ => "",
        };

        return $"{key}+{sizeKey}+{(bitmapSource ? "Src" : "Bmp")}";
    }

    private static Bitmap LoadJumbo(SpecialIcon specialIcon, int desiredSize)
    {
        Icon icon;
        if (specialIcon.DllPath is null)
        {
            _imgList.ImageListSize = SysImageListSize.SHIL_JUMBO;
            icon = _imgList.Icon(specialIcon.Index);
        }
        else
        {
            icon = NativeMethods.ExtractIconByIndex(specialIcon.DllPath, specialIcon.Index, 256);
        }
        Bitmap bitmap = icon.ToBitmap();
        icon.Dispose();

        var usable = FindUsableSize(bitmap);
        if (usable is SysImageListSize.SHIL_JUMBO)
        {
            // we are unable to downscale here, so it will be handled in the UI
            bitmap = ResizeImage(bitmap, new System.Drawing.Size(256, 256), 0, false);
        }
        else if (specialIcon.DllPath is null)
        {
            _imgList.ImageListSize = usable;
            bitmap = ResizeImage(_imgList.Icon(specialIcon.Index).ToBitmap(), new System.Drawing.Size(desiredSize, desiredSize), 0);
        }
        else
        {
            int usableSize = usable switch
            {
                SysImageListSize.SHIL_EXTRALARGE => 48,
                SysImageListSize.SHIL_LARGE => 32,
                _ => 16,
            };
            icon = NativeMethods.ExtractIconByIndex(specialIcon.DllPath, specialIcon.Index, usableSize);
            bitmap = ResizeImage(icon.ToBitmap(), new System.Drawing.Size(desiredSize, desiredSize), 0);
            icon.Dispose();
        }

        return bitmap;
    }

    private static Bitmap LoadJumbo(string lookup, int desiredSize)
    {
        _imgList.ImageListSize = SysImageListSize.SHIL_JUMBO;
        return LoadJumbo(new SpecialIcon(null, _imgList.IconIndex(lookup)), desiredSize);
    }

    /// <summary>
    /// Determines the appropriate image list size category for a bitmap based on the number of columns containing at
    /// least one non-transparent pixel.
    /// </summary>
    /// <remarks>The method evaluates each column in the bitmap and counts those that contain at least one
    /// non-transparent pixel. The resulting count is used to select the most suitable SysImageListSize value. This can
    /// be useful when determining how to display or process icon images of varying sizes.</remarks>
    /// <param name="bitmap">The bitmap to analyze. Cannot be null.</param>
    /// <returns>A value from the SysImageListSize enumeration that indicates the size category of the bitmap: SHIL_JUMBO,
    /// SHIL_EXTRALARGE, SHIL_LARGE, or SHIL_SMALL.</returns>
    private static SysImageListSize FindUsableSize(Bitmap bitmap)
    {
        System.Drawing.Color empty = System.Drawing.Color.FromArgb(0, 0, 0, 0);
        int ValidColumns = 0;

        for (int i = 0; i < bitmap.Width; i++)
        {
            int validPixels = 0;
            for (int j = 0; j < bitmap.Height; j++)
            {
                if (bitmap.GetPixel(i, j) != empty)
                    validPixels++;
            }
            if (validPixels > 0)
                ValidColumns++;
        }

        return ValidColumns switch
        {
            > 48 => SysImageListSize.SHIL_JUMBO,
            > 32 => SysImageListSize.SHIL_EXTRALARGE,
            > 16 => SysImageListSize.SHIL_LARGE,
            _ => SysImageListSize.SHIL_SMALL,
        };
    }

    private static T AddToDictionary<T>(string fileName, IconSize size, int desiredSize, AbstractFile.SpecialFileType specialType = AbstractFile.SpecialFileType.Regular)
    {
        var bitmapSource = typeof(T) == typeof(BitmapSource);
        var key = ReturnKey(fileName, size, specialType, bitmapSource);
        
        if (!iconDic.ContainsKey(key))
            lock (iconDic)
                iconDic.Add(key, bitmapSource
                    ? GetImage(fileName, size, desiredSize, specialType)
                    : GetBitmap(fileName, size, desiredSize, specialType));

        return (T)iconDic[key];
    }

    private static T AddToDictionary<T>(Icon icon, IconSize size, AbstractFile.SpecialFileType specialType)
    {
        var bitmapSource = typeof(T) == typeof(BitmapSource);
        var key = ReturnKey("", size, specialType);

        if (!iconDic.ContainsKey(key))
            lock (iconDic)
                iconDic.Add(key, bitmapSource
                    ? LoadBitmap(icon.ToBitmap())
                    : icon.ToBitmap());

        return (T)iconDic[key];
    }

    private static BitmapSource GetImage(string fileName, IconSize size, int desiredSize, AbstractFile.SpecialFileType specialType = AbstractFile.SpecialFileType.Regular)
        => LoadBitmap(GetBitmap(fileName, size, desiredSize, specialType));

    private static Bitmap GetBitmap(string fileName, IconSize size, int desiredSize, AbstractFile.SpecialFileType specialType = AbstractFile.SpecialFileType.Regular)
    {
        Icon icon;
        var specialIcon = SpecialTypeIndex(specialType, fileName);
        string lookup = specialType.HasFlag(AbstractFile.SpecialFileType.Regular) && Path.GetExtension(fileName) is { Length: > 0 } ext
            ? $"aaa{ext.ToLower()}"
            : fileName;

        switch (size)
        {
            case IconSize.Jumbo or IconSize.Thumbnail:
                return specialIcon.IsValid
                    ? LoadJumbo(specialIcon, desiredSize)
                    : LoadJumbo(lookup, desiredSize);

            case IconSize.ExtraLarge:
                if (specialIcon.IsValid)
                {
                    icon = NativeMethods.ExtractIconByIndex(specialIcon.DllPath, specialIcon.Index, 48);
                }
                else
                {
                    _imgList.ImageListSize = SysImageListSize.SHIL_EXTRALARGE;
                    icon = _imgList.Icon(_imgList.IconIndex(lookup));
                }

                return icon.ToBitmap();

            default:
                icon = specialIcon.IsValid ? GetIconFromIndex(specialIcon, size) : GetFileIcon(lookup, size);

                return icon.ToBitmap();
        }
    }

    private static SpecialIcon SpecialTypeIndex(AbstractFile.SpecialFileType specialType, string fileName = null)
    {
        if (specialType is AbstractFile.SpecialFileType.Folder)
        {
            if (Data.Settings.SpecialFolderIcons
                && DriveHelper.GetCurrentDrive(Data.CurrentPath)?.Type is AbstractDrive.DriveType.Internal)
            {
                return fileName switch
                {
                    "Music" => MusicFolderIcon,
                    "Documents" => DocumentsFolderIcon,
                    "Downloads" or "Download" => DownloadsFolderIcon,
                    "Videos" or "Movies" => VideosFolderIcon,
                    "Pictures" => PicturesFolderIcon,
                    "DCIM" => GalleryIcon,
                    _ => FolderIcon,
                };
            }
            else
                return FolderIcon;
        }

        return specialType switch
        {
            AbstractFile.SpecialFileType.BrokenLink => BrokenLinkIcon,
            AbstractFile.SpecialFileType.Unknown => UnknownIcon,
            AbstractFile.SpecialFileType.LinkOverlay => LinkOverlayIcon,
            _ => SpecialIcon.None,
        };
    }

    private static System.Drawing.Size IconToSize(IconSize size) => size switch
    {
        IconSize.Small => new(16, 16),
        IconSize.Large => new(32, 32),
        IconSize.ExtraLarge => new(48, 48),
        IconSize.Jumbo or IconSize.Thumbnail => new(256, 256),
        _ => throw new NotSupportedException(),
    };

    public static BitmapSource GetImage(string fileName, int iconSize, AbstractFile.SpecialFileType specialType = AbstractFile.SpecialFileType.Regular)
    {
        IconSize size = iconSize switch
        {
            <= 16 => IconSize.Small,
            <= 32 => IconSize.Large,
            <= 48 => IconSize.ExtraLarge,
            _ => IconSize.Jumbo,
        };

        return AddToDictionary<BitmapSource>(fileName, size, iconSize, specialType);
    }

    public static IEnumerable<BitmapSource> GetImage(FilePath file, bool smallIcon = true)
    {
        var size = smallIcon ? IconSize.Small : IconSize.Jumbo;
        var specialType = file.SpecialType;

        if (specialType is 0)
            yield break;
        
        if (specialType.HasFlag(AbstractFile.SpecialFileType.Apk))
        {
            Icon apkIcon = new(Properties.AppGlobal.APK_icon, IconToSize(size));

            yield return AddToDictionary<BitmapSource>(apkIcon, size, AbstractFile.SpecialFileType.Apk);
        }
        else
        {
            // Get icon without link overlay
            yield return AddToDictionary<BitmapSource>(file.FullName, size, smallIcon ? 16 : 96, specialType & ~AbstractFile.SpecialFileType.LinkOverlay);
        }

        if (specialType.HasFlag(AbstractFile.SpecialFileType.LinkOverlay))
        {
            // Get link overlay if required
            yield return AddToDictionary<BitmapSource>(file.FullName, size, smallIcon ? 16 : 96, AbstractFile.SpecialFileType.LinkOverlay);
        }
    }

    private static T GetImage<T>(FilePath file)
    {
        var specialType = file.SpecialType;
        if (specialType.HasFlag(AbstractFile.SpecialFileType.Apk))
        {
            Icon apkIcon = new(Properties.AppGlobal.APK_icon_256px, IconToSize(IconSize.Jumbo));

            return AddToDictionary<T>(apkIcon, IconSize.Jumbo, AbstractFile.SpecialFileType.Apk);
        }
        else
        {
            // Get icon without link overlay
            return AddToDictionary<T>(file.FullName, IconSize.Jumbo, 96, specialType & ~AbstractFile.SpecialFileType.LinkOverlay);
        }
    }

    public static Bitmap GetBitmap(FilePath file)
        => GetImage<Bitmap>(file);

    public static BitmapSource GetBitmapSource(FilePath file)
        => GetImage<BitmapSource>(file);
}
