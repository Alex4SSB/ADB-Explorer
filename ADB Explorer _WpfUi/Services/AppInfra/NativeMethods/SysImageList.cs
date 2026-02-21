// Part of FileToIcon from Code Project article by Leung Yat Chun
// https://www.codeproject.com/Articles/32059/WPF-Filename-To-Icon-Converter
// Used and modified under the LGPLv3 license

namespace Services;

using System.Drawing;
using static ADB_Explorer.Services.NativeMethods;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    #region Public Enumerations
    /// <summary>
    /// Available system image list sizes
    /// </summary>
    public enum SysImageListSize : int
    {
        /// <summary>
        /// System Large Icon Size (typically 32x32)
        /// </summary>
        SHIL_LARGE = 0x0,
        /// <summary>
        /// System Small Icon Size (typically 16x16)
        /// </summary>
        SHIL_SMALL = 0x1,
        /// <summary>
        /// System Extra Large Icon Size (typically 48x48).
        /// Only available under XP; under other OS the
        /// Large Icon ImageList is returned.
        /// </summary>
        SHIL_EXTRALARGE = 0x2,
        /// <summary>
        /// According to GetSystemMetrics
        /// </summary>
        SHIL_SYSSMALL = 0x3,
        /// <summary>
        /// System Jumbo Icon Size (typically 256x256).
        /// Only available in Windows Vista and later.
        /// </summary>
        SHIL_JUMBO = 0x4,
        /// <summary>
        /// The largest valid flag value, for validation purposes.
        /// </summary>
        SHIL_LAST,
    }

    /// <summary>
    /// Flags controlling how the Image List item is drawn
    /// </summary>
    [Flags]
    public enum ImageListDrawItemConstants : int
    {
        /// <summary>
        /// Draw item normally.
        /// </summary>
        ILD_NORMAL = 0x0,
        /// <summary>
        /// Draw item transparently.
        /// </summary>
        ILD_TRANSPARENT = 0x1,
        /// <summary>
        /// Draw item blended with 25% of the specified foreground colour
        /// or the Highlight colour if no foreground colour specified.
        /// </summary>
        ILD_BLEND25 = 0x2,
        /// <summary>
        /// Draw item blended with 50% of the specified foreground colour
        /// or the Highlight colour if no foreground colour specified.
        /// </summary>
        ILD_SELECTED = 0x4,
        /// <summary>
        /// Draw the icon's mask
        /// </summary>
        ILD_MASK = 0x10,
        /// <summary>
        /// Draw the icon image without using the mask
        /// </summary>
        ILD_IMAGE = 0x20,
        /// <summary>
        /// Draw the icon using the ROP specified.
        /// </summary>
        ILD_ROP = 0x40,
        /// <summary>
        /// Preserves the alpha channel in dest. XP only.
        /// </summary>
        ILD_PRESERVEALPHA = 0x1000,
        /// <summary>
        /// Scale the image to cx, cy instead of clipping it.  XP only.
        /// </summary>
        ILD_SCALE = 0x2000,
        /// <summary>
        /// Scale the image to the current DPI of the display. XP only.
        /// </summary>
        ILD_DPISCALE = 0x4000
    }

    /// <summary>
    /// Enumeration containing XP ImageList Draw State options
    /// </summary>
    [Flags]
    public enum ImageListDrawStateConstants : int
    {
        /// <summary>
        /// The image state is not modified. 
        /// </summary>
        ILS_NORMAL = 0x00000000,
        /// <summary>
        /// Adds a glow effect to the icon, which causes the icon to appear to glow 
        /// with a given color around the edges. (Note: does not appear to be
        /// implemented)
        /// </summary>
        ILS_GLOW = 0x00000001, //The color for the glow effect is passed to the IImageList::Draw method in the crEffect member of IMAGELISTDRAWPARAMS. 
        /// <summary>
        /// Adds a drop shadow effect to the icon. (Note: does not appear to be
        /// implemented)
        /// </summary>
        ILS_SHADOW = 0x00000002, //The color for the drop shadow effect is passed to the IImageList::Draw method in the crEffect member of IMAGELISTDRAWPARAMS. 
        /// <summary>
        /// Saturates the icon by increasing each color component 
        /// of the RGB triplet for each pixel in the icon. (Note: only ever appears
        /// to result in a completely unsaturated icon)
        /// </summary>
        ILS_SATURATE = 0x00000004, // The amount to increase is indicated by the frame member in the IMAGELISTDRAWPARAMS method. 
        /// <summary>
        /// Alpha blends the icon. Alpha blending controls the transparency 
        /// level of an icon, according to the value of its alpha channel. 
        /// (Note: does not appear to be implemented).
        /// </summary>
        ILS_ALPHA = 0x00000008 //The value of the alpha channel is indicated by the frame member in the IMAGELISTDRAWPARAMS method. The alpha channel can be from 0 to 255, with 0 being completely transparent, and 255 being completely opaque. 
    }
    #endregion

    #region SysImageList
    /// <summary>
    /// Summary description for SysImageList.
    /// </summary>
    public class SysImageList : IDisposable
    {
        #region UnmanagedCode

        [DllImport("ComCtl32.dll")]
        private static extern int ImageList_Draw(
            HANDLE hIml,
            int i,
            HANDLE hdcDst,
            int x,
            int y,
            int fStyle);

        [DllImport("ComCtl32.dll")]
        private static extern int ImageList_DrawIndirect(
            ref IMAGELISTDRAWPARAMS pimldp);

        [DllImport("ComCtl32.dll")]
        private static extern HANDLE ImageList_GetIcon(
            HANDLE himl,
            int i,
            int flags);

        /// <summary>
        /// SHGetImageList is not exported correctly in XP.  See KB316931
        /// http://support.microsoft.com/default.aspx?scid=kb;EN-US;Q316931
        /// Apparently (and hopefully) ordinal 727 isn't going to change.
        /// </summary>
        [DllImport("Shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(
            int iImageList,
            ref Guid riid,
            ref IImageList ppv
            );

        [DllImport("Shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageListHandle(
            int iImageList,
            ref Guid riid,
            ref HANDLE handle
            );

        #endregion

        #region Private ImageList structures
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGELISTDRAWPARAMS
        {
            public int cbSize;
            public HANDLE himl;
            public int i;
            public HANDLE hdcDst;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public int xBitmap;        // x offest from the upperleft of bitmap
            public int yBitmap;        // y offset from the upperleft of bitmap
            public int rgbBk;
            public int rgbFg;
            public ImageListDrawItemConstants fStyle;
            public int dwRop;
            public ImageListDrawStateConstants fState;
            public int Frame;
            public int crEffect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGEINFO
        {
            public HANDLE hbmImage;
            public HANDLE hbmMask;
            public int Unused1;
            public int Unused2;
            public RECT rcImage;
        }
        
        #endregion

        #region Private ImageList COM Interop (XP)
        [ComImportAttribute()]
        [GuidAttribute("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        
        interface IImageList
        {
            [PreserveSig]
            int Add(
                HANDLE hbmImage,
                HANDLE hbmMask,
                ref int pi);

            [PreserveSig]
            int ReplaceIcon(
                int i,
                HANDLE hicon,
                ref int pi);

            [PreserveSig]
            int SetOverlayImage(
                int iImage,
                int iOverlay);

            [PreserveSig]
            int Replace(
                int i,
                HANDLE hbmImage,
                HANDLE hbmMask);

            [PreserveSig]
            int AddMasked(
                HANDLE hbmImage,
                int crMask,
                ref int pi);

            [PreserveSig]
            int Draw(
                ref IMAGELISTDRAWPARAMS pimldp);

            [PreserveSig]
            int Remove(
                int i);

            [PreserveSig]
            int GetIcon(
                int i,
                int flags,
                ref HANDLE picon);

            [PreserveSig]
            int GetImageInfo(
                int i,
                ref IMAGEINFO pImageInfo);

            [PreserveSig]
            int Copy(
                int iDst,
                IImageList punkSrc,
                int iSrc,
                int uFlags);

            [PreserveSig]
            int Merge(
                int i1,
                IImageList punk2,
                int i2,
                int dx,
                int dy,
                ref Guid riid,
                ref HANDLE ppv);

            [PreserveSig]
            int Clone(
                ref Guid riid,
                ref HANDLE ppv);

            [PreserveSig]
            int GetImageRect(
                int i,
                ref RECT prc);

            [PreserveSig]
            int GetIconSize(
                ref int cx,
                ref int cy);

            [PreserveSig]
            int SetIconSize(
                int cx,
                int cy);

            [PreserveSig]
            int GetImageCount(
                ref int pi);

            [PreserveSig]
            int SetImageCount(
                int uNewCount);

            [PreserveSig]
            int SetBkColor(
                int clrBk,
                ref int pclr);

            [PreserveSig]
            int GetBkColor(
                ref int pclr);

            [PreserveSig]
            int BeginDrag(
                int iTrack,
                int dxHotspot,
                int dyHotspot);

            [PreserveSig]
            int EndDrag();

            [PreserveSig]
            int DragEnter(
                HANDLE hwndLock,
                int x,
                int y);

            [PreserveSig]
            int DragLeave(
                HANDLE hwndLock);

            [PreserveSig]
            int DragMove(
                int x,
                int y);

            [PreserveSig]
            int SetDragCursorImage(
                ref IImageList punk,
                int iDrag,
                int dxHotspot,
                int dyHotspot);

            [PreserveSig]
            int DragShowNolock(
                int fShow);

            [PreserveSig]
            int GetDragImage(
                ref POINT ppt,
                ref POINT pptHotspot,
                ref Guid riid,
                ref HANDLE ppv);

            [PreserveSig]
            int GetItemFlags(
                int i,
                ref int dwFlags);

            [PreserveSig]
            int GetOverlayImage(
                int iOverlay,
                ref int piIndex);
        };
        #endregion

        #region Member Variables
        private HANDLE hIml = IntPtr.Zero;
        private IImageList iImageList;
        private SysImageListSize size = SysImageListSize.SHIL_SMALL;
        private bool disposed;
        #endregion

        #region Implementation

        #region Properties
        /// <summary>
        /// Gets the hImageList handle
        /// </summary>
        public HANDLE Handle => hIml;

        /// <summary>
        /// Gets/sets the size of System Image List to retrieve.
        /// </summary>
        public SysImageListSize ImageListSize
        {
            get => size;
            set
            {
                size = value;
                Create();
            }

        }

        #endregion

        #region Methods
        /// <summary>
        /// Returns a GDI+ copy of the icon from the ImageList
        /// at the specified index.
        /// </summary>
        /// <param name="index">The index to get the icon for</param>
        /// <returns>The specified icon</returns>
        public Icon Icon(int index)
        {
            Icon icon = null;

            HANDLE hIcon = IntPtr.Zero;
            if (iImageList == null)
            {
                hIcon = ImageList_GetIcon(
                    hIml,
                    index,
                    (int)ImageListDrawItemConstants.ILD_TRANSPARENT);

            }
            else
            {
                iImageList.GetIcon(
                    index,
                    (int)ImageListDrawItemConstants.ILD_TRANSPARENT,
                    ref hIcon);
            }

            if (hIcon != IntPtr.Zero)
            {
                icon = System.Drawing.Icon.FromHandle(hIcon);
            }
            return icon;
        }

        /// <summary>
        /// Return the index of the icon for the specified file, always using 
        /// the cached version where possible.
        /// </summary>
        /// <param name="fileName">Filename to get icon for</param>
        /// <returns>Index of the icon</returns>
        public int IconIndex(string fileName) => IconIndex(fileName, false);

        /// <summary>
        /// Returns the index of the icon for the specified file
        /// </summary>
        /// <param name="fileName">Filename to get icon for</param>
        /// <param name="forceLoadFromDisk">If True, then hit the disk to get the icon,
        /// otherwise only hit the disk if no cached icon is available.</param>
        /// <returns>Index of the icon</returns>
        public int IconIndex(
            string fileName,
            bool forceLoadFromDisk)
        {
            return IconIndex(
                fileName,
                forceLoadFromDisk,
                FileInfoFlags.SHGFI_LARGEICON);
        }

        /// <summary>
        /// Returns the index of the icon for the specified file
        /// </summary>
        /// <param name="fileName">Filename to get icon for</param>
        /// <param name="forceLoadFromDisk">If True, then hit the disk to get the icon,
        /// otherwise only hit the disk if no cached icon is available.</param>
        /// <param name="iconState">Flags specifying the state of the icon
        /// returned.</param>
        /// <returns>Index of the icon</returns>
        public int IconIndex(
            string fileName,
            bool forceLoadFromDisk,
            FileInfoFlags iconState
            )
        {
            var dwFlags = FileInfoFlags.SHGFI_SYSICONINDEX;
            FileFlagsAndAttributes dwAttr;
            if (size == SysImageListSize.SHIL_SMALL)
            {
                dwFlags |= FileInfoFlags.SHGFI_SMALLICON;
            }

            // We can choose whether to access the disk or not. If you don't
            // hit the disk, you may get the wrong icon if the icon is
            // not cached. Also only works for files.
            if (!forceLoadFromDisk)
            {
                dwFlags |= FileInfoFlags.SHGFI_USEFILEATTRIBUTES;
                dwAttr = FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL;
            }
            else
            {
                dwAttr = 0;
            }

            // sFileSpec can be any file. You can specify a
            // file that does not exist and still get the
            // icon, for example sFileSpec = "C:\PANTS.DOC"

            return GetIconIndex(fileName, dwAttr, dwFlags, iconState);
        }

        /// <summary>
        /// Draws an image
        /// </summary>
        /// <param name="hdc">Device context to draw to</param>
        /// <param name="index">Index of image to draw</param>
        /// <param name="x">X Position to draw at</param>
        /// <param name="y">Y Position to draw at</param>
        public void DrawImage(
            HANDLE hdc,
            int index,
            int x,
            int y
            )
        {
            DrawImage(hdc, index, x, y, ImageListDrawItemConstants.ILD_TRANSPARENT);
        }

        /// <summary>
        /// Draws an image using the specified flags
        /// </summary>
        /// <param name="hdc">Device context to draw to</param>
        /// <param name="index">Index of image to draw</param>
        /// <param name="x">X Position to draw at</param>
        /// <param name="y">Y Position to draw at</param>
        /// <param name="flags">Drawing flags</param>
        public void DrawImage(
            HANDLE hdc,
            int index,
            int x,
            int y,
            ImageListDrawItemConstants flags
            )
        {
            if (iImageList == null)
            {
                _ = ImageList_Draw(
                    hIml,
                    index,
                    hdc,
                    x,
                    y,
                    (int)flags);
            }
            else
            {
                IMAGELISTDRAWPARAMS pimldp = new()
                {
                    hdcDst = hdc,
                    cbSize = Marshal.SizeOf(typeof(IMAGELISTDRAWPARAMS)),
                    i = index,
                    x = x,
                    y = y,
                    rgbFg = -1,
                    fStyle = flags,
                };
                
                iImageList.Draw(ref pimldp);
            }

        }

        /// <summary>
        /// Draws an image using the specified flags and specifies
        /// the size to clip to (or to stretch to if ILD_SCALE
        /// is provided).
        /// </summary>
        /// <param name="hdc">Device context to draw to</param>
        /// <param name="index">Index of image to draw</param>
        /// <param name="x">X Position to draw at</param>
        /// <param name="y">Y Position to draw at</param>
        /// <param name="flags">Drawing flags</param>
        /// <param name="cx">Width to draw</param>
        /// <param name="cy">Height to draw</param>
        public void DrawImage(
            HANDLE hdc,
            int index,
            int x,
            int y,
            ImageListDrawItemConstants flags,
            int cx,
            int cy
            )
        {
            IMAGELISTDRAWPARAMS pimldp = new()
            {
                hdcDst = hdc,
                cbSize = Marshal.SizeOf(typeof(IMAGELISTDRAWPARAMS)),
                i = index,
                x = x,
                y = y,
                cx = cx,
                cy = cy,
                fStyle = flags
            };

            if (iImageList == null)
            {
                pimldp.himl = hIml;
                _ = ImageList_DrawIndirect(ref pimldp);
            }
            else
            {
                iImageList.Draw(ref pimldp);
            }
        }

        /// <summary>
        /// Draws an image using the specified flags and state on XP systems.
        /// </summary>
        /// <param name="hdc">Device context to draw to</param>
        /// <param name="index">Index of image to draw</param>
        /// <param name="x">X Position to draw at</param>
        /// <param name="y">Y Position to draw at</param>
        /// <param name="flags">Drawing flags</param>
        /// <param name="cx">Width to draw</param>
        /// <param name="cy">Height to draw</param>
        /// <param name="foreColor">Fore colour to blend with when using the 
        /// ILD_SELECTED or ILD_BLEND25 flags</param>
        /// <param name="stateFlags">State flags</param>
        /// <param name="glowOrShadowColor">If stateFlags include ILS_GLOW, then
        /// the colour to use for the glow effect.  Otherwise if stateFlags includes 
        /// ILS_SHADOW, then the colour to use for the shadow.</param>
        /// <param name="saturateColorOrAlpha">If stateFlags includes ILS_ALPHA,
        /// then the alpha component is applied to the icon. Otherwise if 
        /// ILS_SATURATE is included, then the (R,G,B) components are used
        /// to saturate the image.</param>
        public void DrawImage(
            HANDLE hdc,
            int index,
            int x,
            int y,
            ImageListDrawItemConstants flags,
            int cx,
            int cy,
            Color foreColor,
            ImageListDrawStateConstants stateFlags,
            Color saturateColorOrAlpha,
            Color glowOrShadowColor
            )
        {
            IMAGELISTDRAWPARAMS pimldp = new()
            {
                hdcDst = hdc,
                cbSize = Marshal.SizeOf(typeof(IMAGELISTDRAWPARAMS)),
                i = index,
                x = x,
                y = y,
                cx = cx,
                cy = cy,
                rgbFg = Color.FromArgb(0, foreColor.R, foreColor.G, foreColor.B).ToArgb(),
                fStyle = flags,
                fState = stateFlags
            };
            if ((stateFlags & ImageListDrawStateConstants.ILS_ALPHA) ==
                ImageListDrawStateConstants.ILS_ALPHA)
            {
                // Set the alpha:
                pimldp.Frame = saturateColorOrAlpha.A;
            }
            else if ((stateFlags & ImageListDrawStateConstants.ILS_SATURATE) ==
                ImageListDrawStateConstants.ILS_SATURATE)
            {
                // discard alpha channel:
                saturateColorOrAlpha = Color.FromArgb(0,
                    saturateColorOrAlpha.R,
                    saturateColorOrAlpha.G,
                    saturateColorOrAlpha.B);
                // set the saturate color
                pimldp.Frame = saturateColorOrAlpha.ToArgb();
            }
            glowOrShadowColor = Color.FromArgb(0,
                glowOrShadowColor.R,
                glowOrShadowColor.G,
                glowOrShadowColor.B);
            pimldp.crEffect = glowOrShadowColor.ToArgb();
            if (iImageList == null)
            {
                pimldp.himl = hIml;
                _= ImageList_DrawIndirect(ref pimldp);
            }
            else
            {

                iImageList.Draw(ref pimldp);
            }
        }

        /// <summary>
        /// Creates the SystemImageList
        /// </summary>
        private void Create()
        {
            // forget last image list if any:
            hIml = IntPtr.Zero;

            // Get the System IImageList object from the Shell:
            Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            _ = SHGetImageList(
                (int)size,
                ref iidImageList,
                ref iImageList
                );

            // the image list handle is the IUnknown pointer, but 
            // using Marshal.GetIUnknownForObject doesn't return
            // the right value.  It really doesn't hurt to make
            // a second call to get the handle:
            _ = SHGetImageListHandle((int)size, ref iidImageList, ref hIml);

            // Used to contain code to support OSs before Windows XP
            // ADB Explorer requires at least Windows 10 build 18362
        }
        #endregion

        #region Constructor, Dispose, Destructor
        /// <summary>
        /// Creates a Small Icons SystemImageList 
        /// </summary>
        public SysImageList()
        {
            Create();
        }
        /// <summary>
        /// Creates a SystemImageList with the specified size
        /// </summary>
        /// <param name="size">Size of System ImageList</param>
        public SysImageList(SysImageListSize size)
        {
            this.size = size;
            Create();
        }

        /// <summary>
        /// Clears up any resources associated with the SystemImageList
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Clears up any resources associated with the SystemImageList
        /// when disposing is true.
        /// </summary>
        /// <param name="disposing">Whether the object is being disposed</param>
        public virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (iImageList != null)
                    {
                        Marshal.ReleaseComObject(iImageList);
                    }
                    iImageList = null;
                }
            }
            disposed = true;
        }
        /// <summary>
        /// Finalise for SysImageList
        /// </summary>
        ~SysImageList()
        {
            Dispose(false);
        }

    }
    #endregion

    #endregion

    #endregion
}
