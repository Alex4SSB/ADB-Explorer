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

    private readonly record struct SpecialIcon(string? DllPath, int Index)
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

    private static readonly System.Drawing.Color Gray232 = System.Drawing.Color.FromArgb(232, 232, 232);

    private readonly record struct IconCacheKey(string IconId, IconSize Size, int DesiredSize);
    private static readonly Dictionary<IconCacheKey, BitmapSource> iconDic = [];
    private static readonly Dictionary<string, Rectangle> contentBoundsCache = [];
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
        => NativeMethods.ExtractIconByIndex(specialIcon.DllPath!, specialIcon.Index, size);

    private static Bitmap ResizeImage(Bitmap imgToResize, Rectangle sourceRect, int desiredSize)
    {
        const int spacing = 1;

        if (sourceRect.Width > desiredSize * 0.75 || sourceRect.Height > desiredSize * 0.75)
        {
            return imgToResize;
        }

        int sourceWidth = sourceRect.Width;
        int sourceHeight = sourceRect.Height;

        float scale = 1f;
        if (sourceWidth > desiredSize || sourceHeight > desiredSize)
        {
            scale = Math.Min((float)desiredSize / sourceWidth, (float)desiredSize / sourceHeight);
        }

        int renderedWidth = (int)Math.Round(sourceRect.Width * scale);
        int renderedHeight = (int)Math.Round(sourceRect.Height * scale);

        int leftOffset = (desiredSize - renderedWidth) / 2;
        int topOffset = (desiredSize - renderedHeight) / 2;

        Bitmap b = new(desiredSize, desiredSize);
        using Graphics g = Graphics.FromImage(b);
        g.Clear(System.Drawing.Color.Transparent);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;

        using var pen = new System.Drawing.Pen(Gray232);
        int r = 4;
        int x = spacing;
        int y = spacing;
        int w = desiredSize - spacing * 2 - 1;
        int h = desiredSize - spacing * 2 - 1;

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();

        g.DrawPath(pen, path);

        g.DrawImage(imgToResize, new Rectangle(leftOffset, topOffset, renderedWidth, renderedHeight), sourceRect, GraphicsUnit.Pixel);

        return b;
    }

    private static Rectangle GetContentBounds(Bitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < bitmap.Height; y++)
        {
            int rowNonEmpty = 0;
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0)
                {
                    rowNonEmpty++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
            return Rectangle.Empty;

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
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

    private static string ComputeIconId(string fileName, AbstractFile.SpecialFileType specialType)
    {
        if (specialType.HasFlag(AbstractFile.SpecialFileType.Regular))
            return Path.GetExtension(fileName).ToLower();

        var specialIcon = SpecialTypeIndex(specialType, fileName);
        return specialIcon.IsValid
            ? $"{specialIcon.DllPath}_{specialIcon.Index}"
            : Enum.GetName(specialType) ?? specialType.ToString();
    }

    private static IconCacheKey ReturnKey(string fileName, IconSize size, int desiredSize, AbstractFile.SpecialFileType specialType)
    {
        int keyedSize = size is IconSize.Jumbo or IconSize.Thumbnail ? desiredSize : 0;
        return new IconCacheKey(ComputeIconId(fileName, specialType), size, keyedSize);
    }

    private static Bitmap LoadJumbo(SpecialIcon specialIcon, string iconId, int desiredSize)
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

        if (!contentBoundsCache.TryGetValue(iconId, out var bounds))
        {
            bounds = GetContentBounds(bitmap);
            lock (contentBoundsCache)
                contentBoundsCache.TryAdd(iconId, bounds);
        }

        return ResizeImage(bitmap, bounds, desiredSize);
    }

    private static Bitmap LoadJumbo(string lookup, string iconId, int desiredSize)
    {
        _imgList.ImageListSize = SysImageListSize.SHIL_JUMBO;
        return LoadJumbo(new SpecialIcon(null, _imgList.IconIndex(lookup)), iconId, desiredSize);
    }

    private static BitmapSource AddToDictionary(string fileName, IconSize size, int desiredSize, AbstractFile.SpecialFileType specialType = AbstractFile.SpecialFileType.Regular)
    {
        if (size is IconSize.Jumbo or IconSize.Thumbnail)
        {
            var iconId = ComputeIconId(fileName, specialType);
            var canonicalKey = new IconCacheKey(iconId, size, 0);
            var sizedKey = new IconCacheKey(iconId, size, desiredSize);

            if (contentBoundsCache.TryGetValue(iconId, out var bounds))
            {
                // Bounds are known: pick the right key without a redundant probe
                bool isUnmodified = bounds.Width > desiredSize * 0.75 || bounds.Height > desiredSize * 0.75;
                var fastKey = isUnmodified ? canonicalKey : sizedKey;
                if (iconDic.TryGetValue(fastKey, out var fastCached))
                    return fastCached;
            }
            else
            {
                if (iconDic.TryGetValue(canonicalKey, out var canonicalCached))
                    return canonicalCached;

                if (iconDic.TryGetValue(sizedKey, out var sizedCached))
                    return sizedCached;
            }

            var bitmap = GetBitmap(fileName, size, desiredSize, specialType);
            bool wasUnmodified = bitmap.Width != desiredSize || bitmap.Height != desiredSize;
            var storeKey = wasUnmodified ? canonicalKey : sizedKey;
            BitmapSource value = LoadBitmap(bitmap);

            lock (iconDic)
                iconDic.TryAdd(storeKey, value);

            return iconDic[storeKey];
        }

        var key = ReturnKey(fileName, size, desiredSize, specialType);

        if (!iconDic.ContainsKey(key))
            lock (iconDic)
                iconDic.Add(key, GetImage(fileName, size, desiredSize, specialType));

        return iconDic[key];
    }

    private static BitmapSource AddToDictionary(Icon icon, IconSize size, AbstractFile.SpecialFileType specialType)
    {
        var key = ReturnKey("", size, 0, specialType);

        if (!iconDic.ContainsKey(key))
            lock (iconDic)
                iconDic.Add(key, LoadBitmap(icon.ToBitmap()));

        return iconDic[key];
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
            {
                var iconId = ComputeIconId(fileName, specialType);
                return specialIcon.IsValid
                    ? LoadJumbo(specialIcon, iconId, desiredSize)
                    : LoadJumbo(lookup, iconId, desiredSize);
            }

            case IconSize.ExtraLarge:
                if (specialIcon.IsValid)
                {
                    icon = NativeMethods.ExtractIconByIndex(specialIcon.DllPath!, specialIcon.Index, 48);
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

    private static SpecialIcon SpecialTypeIndex(AbstractFile.SpecialFileType specialType, string? fileName = null)
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

    private static IconSize SizeToIconSize(int iconSize) => iconSize switch
    {
        <= 16 => IconSize.Small,
        <= 32 => IconSize.Large,
        <= 48 => IconSize.ExtraLarge,
        _ => IconSize.Jumbo,
    };

    public static IEnumerable<BitmapSource> GetImage(FilePath file, int iconSize = 16)
    {
        var size = SizeToIconSize(iconSize);
        var specialType = file.SpecialType;

        if (specialType is 0)
            yield break;
        
        if (specialType.HasFlag(AbstractFile.SpecialFileType.Apk))
        {
            Icon apkIcon = new(Properties.AppGlobal.APK_icon, IconToSize(size));

            yield return AddToDictionary(apkIcon, size, AbstractFile.SpecialFileType.Apk);
        }
        else
        {
            // Get icon without link overlay
            yield return AddToDictionary(file.FullName, size, iconSize, specialType & ~AbstractFile.SpecialFileType.LinkOverlay);
        }

        if (specialType.HasFlag(AbstractFile.SpecialFileType.LinkOverlay))
        {
            // Get link overlay if required
            yield return AddToDictionary(file.FullName, size, iconSize, AbstractFile.SpecialFileType.LinkOverlay);
        }
    }
}
