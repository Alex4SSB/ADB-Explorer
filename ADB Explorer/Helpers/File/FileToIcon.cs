// Part of FileToIcon from Code Project article by Leung Yat Chun
// https://www.codeproject.com/Articles/32059/WPF-Filename-To-Icon-Converter
// Used and modified under the LGPLv3 license

using ADB_Explorer.Services;
using System.Drawing;
using static Services.NativeMethods;

namespace ADB_Explorer.Helpers;

public class FileToIconConverter
{
    private static readonly string[] imageFilter = new[] { ".jpg", ".jpeg", ".png", ".gif" };
    private static readonly string exeFilter = ".exe,.lnk";

    private readonly int defaultsize;

    public enum IconSize : uint
    {
        Large,
        Small,
        ExtraLarge,
        Jumbo,
        Thumbnail,
    }

    private class ThumbnailInfo
    {
        public IconSize iconsize;
        public WriteableBitmap bitmap;
        public string fullPath;
        public ThumbnailInfo(WriteableBitmap b, string path, IconSize size)
        {
            bitmap = b;
            fullPath = path;
            iconsize = size;
        }
    }

    // <summary>
    /// Return large file icon of the specified file.
    /// </summary>
    internal static Icon GetFileIcon(string fileName, IconSize size)
    {
        NativeMethods.FileInfoFlags flags = NativeMethods.FileInfoFlags.SHGFI_SYSICONINDEX;
        
        if (!fileName.Contains(':'))
            flags = flags | NativeMethods.FileInfoFlags.SHGFI_USEFILEATTRIBUTES;

        if (size == IconSize.Small)
            flags = flags | NativeMethods.FileInfoFlags.SHGFI_SMALLICON;
        
        return NativeMethods.GetIcon(fileName, flags);
    }

    #region Static Tools

    private static void CopyBitmap(BitmapSource source, WriteableBitmap target, bool useDispatcher)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * ((source.Format.BitsPerPixel + 7) / 8);

        byte[] bits = new byte[height * stride];
        source.CopyPixels(bits, stride, 0);
        source = null;

        Action method = () =>
        {
            var delta = target.Height - height;
            var newWidth = width > target.Width ? (int)target.Width : width;
            var newHeight = height > target.Height ? (int)target.Height : height;
            Int32Rect outRect = new Int32Rect(0, (int)(delta >= 0 ? delta : 0) / 2, newWidth, newWidth);
            try
            {
                target.WritePixels(outRect, bits, stride, 0);
            }
            catch
            { }
        };

        if (useDispatcher)
        {
            target.Dispatcher.BeginInvoke(DispatcherPriority.Background, method);
        }
        else
        {
            method();
        }
    }

    private static System.Drawing.Size GetDefaultSize(IconSize size) => size switch
    {
        IconSize.Thumbnail or IconSize.Jumbo => new(256, 256),
        IconSize.ExtraLarge => new(48, 48),
        IconSize.Large => new(32, 32),
        _ => new(16, 16),
    };

    private static Bitmap ResizeImage(Bitmap imgToResize, System.Drawing.Size size, int spacing, bool addBorder = true)
    {
        int destWidth = imgToResize.Width;
        int destHeight = imgToResize.Height;

        int leftOffset = (size.Width - destWidth) / 2;
        int topOffset = (size.Height - destHeight) / 2;

        Bitmap b = new Bitmap(size.Width, size.Height);
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
            return Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty,
               BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.MDeleteObject(hBitmap);
        }
    }

    private static bool IsImage(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        if (ext == "")
            return false;
        return imageFilter.Contains(ext) && File.Exists(fileName);
    }

    private static bool IsExecutable(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        if (ext == "")
            return false;
        return exeFilter.Contains(ext) && File.Exists(fileName);
    }

    private static bool IsFolder(string path)
    {
        return path.EndsWith("\\") || Directory.Exists(path);
    }

    private static string ReturnKey(string fileName, IconSize size)
    {
        var key = Path.GetExtension(fileName).ToLower();

        if (IsExecutable(fileName)
            || IsImage(fileName) && size == IconSize.Thumbnail
            || IsFolder(fileName))
            key = fileName.ToLower();

        return size switch
        {
            IconSize.Thumbnail => key + (IsImage(fileName) ? "+T" : "+J"),
            IconSize.Jumbo => key + "+J",
            IconSize.ExtraLarge => key + "+XL",
            IconSize.Large => key + "+L",
            IconSize.Small => key + "+S",
            _ => key,
        };
    }
    #endregion

    #region Static Cache
    private static readonly Dictionary<string, ImageSource> iconDic = new Dictionary<string, ImageSource>();
    private static readonly SysImageList _imgList = new SysImageList(SysImageListSize.SHIL_JUMBO);

    private static Bitmap LoadJumbo(string lookup, int desiredSize)
    {
        // Used to contain code to support OSs before Windows Vista
        // ADB Explorer requires at least Windows 10 build 18362

        _imgList.ImageListSize = SysImageListSize.SHIL_JUMBO;
        Icon icon = _imgList.Icon(_imgList.IconIndex(lookup, IsFolder(lookup)));
        Bitmap bitmap = icon.ToBitmap();
        icon.Dispose();

        var usable = FindUsableSize(bitmap);
        if (usable is SysImageListSize.SHIL_JUMBO)
        {
            // we are unable to downscale here, so it will be handled in the UI
            bitmap = ResizeImage(bitmap, new System.Drawing.Size(256, 256), 0, false);
        }
        else
        {
            _imgList.ImageListSize = usable;
            bitmap = ResizeImage(_imgList.Icon(_imgList.IconIndex(lookup)).ToBitmap(), new System.Drawing.Size(desiredSize, desiredSize), 0);
        }

        return bitmap;
    }

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

    #endregion

    #region Instance Cache
    private static readonly Dictionary<string, ImageSource> thumbDic = new Dictionary<string, ImageSource>();

    public static void ClearInstanceCache()
    {
        thumbDic.Clear();
    }

    private void PollIconCallback(object state)
    {
        ThumbnailInfo input = state as ThumbnailInfo;
        string fileName = input.fullPath;
        WriteableBitmap writeBitmap = input.bitmap;
        IconSize size = input.iconsize;

        Bitmap origBitmap = GetFileIcon(fileName, size).ToBitmap();
        Bitmap inputBitmap;

        if (size == IconSize.Jumbo || size == IconSize.Thumbnail)
            inputBitmap = ResizeImage(origBitmap, GetDefaultSize(size), 5);
        else inputBitmap = ResizeImage(origBitmap, GetDefaultSize(size), 0);

        BitmapSource inputBitmapSource = LoadBitmap(inputBitmap);
        origBitmap.Dispose();
        inputBitmap.Dispose();

        CopyBitmap(inputBitmapSource, writeBitmap, true);
    }

    private void PollThumbnailCallback(object state)
    {
        //Non UIThread
        ThumbnailInfo input = state as ThumbnailInfo;
        string fileName = input.fullPath;
        WriteableBitmap writeBitmap = input.bitmap;
        IconSize size = input.iconsize;

        try
        {
            Bitmap origBitmap = new Bitmap(fileName);
            Bitmap inputBitmap = ResizeImage(origBitmap, GetDefaultSize(size), 5);
            BitmapSource inputBitmapSource = LoadBitmap(inputBitmap);
            origBitmap.Dispose();
            inputBitmap.Dispose();

            CopyBitmap(inputBitmapSource, writeBitmap, true);
        }
        catch { }
    }

    private ImageSource AddToDic(string fileName, IconSize size, int desiredSize)
    {
        string key = ReturnKey(fileName, size);

        if (size == IconSize.Thumbnail || IsExecutable(fileName))
        {
            if (!thumbDic.ContainsKey(key))
                lock (thumbDic)
                    thumbDic.Add(key, GetImage(fileName, size, desiredSize));

            return thumbDic[key];
        }
        else
        {
            if (!iconDic.ContainsKey(key))
                lock (iconDic)
                    iconDic.Add(key, GetImage(fileName, size, desiredSize));
            return iconDic[key];
        }
    }

    public ImageSource GetImage(string fileName, int iconSize)
    {
        IconSize size;

        if (iconSize <= 16) size = IconSize.Small;
        else if (iconSize <= 32) size = IconSize.Large;
        else if (iconSize <= 48) size = IconSize.ExtraLarge;
        else if (iconSize <= 72) size = IconSize.Jumbo;
        else size = IconSize.Thumbnail;

        return AddToDic(fileName, size, iconSize);
    }

    #endregion

    #region Instance Tools

    private BitmapSource GetImage(string fileName, IconSize size, int desiredSize)
    {
        Icon icon;
        string key = ReturnKey(fileName, size);
        string lookup = "aaa" + Path.GetExtension(fileName).ToLower();
        if (!key.StartsWith("."))
            lookup = fileName;

        if (IsExecutable(fileName))
        {
            WriteableBitmap bitmap = new WriteableBitmap(AddToDic("aaa.exe", size, desiredSize) as BitmapSource);
            ThreadPool.QueueUserWorkItem(new WaitCallback(PollIconCallback), new ThumbnailInfo(bitmap, fileName, size));
            return bitmap;
        }

        else
            switch (size)
            {
                case IconSize.Thumbnail:
                    if (IsImage(fileName))
                    {
                        //Load as jumbo icon first.                         
                        WriteableBitmap bitmap = new WriteableBitmap(AddToDic(fileName, IconSize.Jumbo, desiredSize) as BitmapSource);
                        ThreadPool.QueueUserWorkItem(new WaitCallback(PollThumbnailCallback), new ThumbnailInfo(bitmap, fileName, size));
                        return bitmap;
                    }
                    else
                    {
                        return GetImage(lookup, IconSize.Jumbo, desiredSize);
                    }
                case IconSize.Jumbo:
                    return LoadBitmap(LoadJumbo(lookup, desiredSize));
                case IconSize.ExtraLarge:
                    _imgList.ImageListSize = SysImageListSize.SHIL_EXTRALARGE;
                    icon = _imgList.Icon(_imgList.IconIndex(lookup, IsFolder(fileName)));
                    return LoadBitmap(icon.ToBitmap());
                default:
                    icon = GetFileIcon(lookup, size);
                    return LoadBitmap(icon.ToBitmap());
            }
    }

    #endregion

    public FileToIconConverter()
    {
        defaultsize = 48;
    }
}
