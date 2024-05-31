namespace ADB_Explorer.Services;

using ADB_Explorer.Helpers;
using System.Drawing;
using System.Runtime.InteropServices.ComTypes;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

using HANDLE = IntPtr;

public static partial class NativeMethods
{
    #region Constants

    public const int MAX_PATH = 260;

    public const int DRAGDROP_S_DROP = 0x00040100;
    public const int DRAGDROP_S_CANCEL = 0x00040101;
    public const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;

    public const int DV_E_DVASPECT = -2147221397;
    public const int DV_E_FORMATETC = -2147221404;
    public const int DV_E_TYMED = -2147221399;
    public const int E_FAIL = -2147467259;
    public const int OLE_E_ADVISENOTSUPPORTED = -2147221501;

    public const int S_OK = 0;
    public const int S_FALSE = 1;

    public const int VARIANT_FALSE = 0;
    public const int VARIANT_TRUE = -1;

    public const string CFSTR_FILECONTENTS = "FileContents";
    public const string CFSTR_FILEDESCRIPTORW = "FileGroupDescriptorW";
    public const string CFSTR_PASTESUCCEEDED = "Paste Succeeded";
    public const string CFSTR_PERFORMEDDROPEFFECT = "Performed DropEffect";
    public const string CFSTR_PREFERREDDROPEFFECT = "Preferred DropEffect";

    #endregion

    #region Enums

    [Flags]
    public enum FileInfoFlags : UInt32
    {
        SHGFI_ICON = 0x100,
        SHGFI_DISPLAYNAME = 0x200,
        SHGFI_TYPENAME = 0x400,
        SHGFI_ATTRIBUTES = 0x800,
        SHGFI_ICONLOCATION = 0x1000,
        SHGFI_EXETYPE = 0x2000,
        SHGFI_SYSICONINDEX = 0x4000,
        SHGFI_LINKOVERLAY = 0x8000,
        SHGFI_SELECTED = 0x10000,
        SHGFI_ATTR_SPECIFIED = 0x20000,
        SHGFI_LARGEICON = 0x0,
        SHGFI_SMALLICON = 0x1,
        SHGFI_OPENICON = 0x2,
        SHGFI_SHELLICONSIZE = 0x4,
        SHGFI_PIDL = 0x8,
        SHGFI_USEFILEATTRIBUTES = 0x10,
        SHGFI_ADDOVERLAYS = 0x000000020,
        SHGFI_OVERLAYINDEX = 0x000000040
    }

    // https://github.com/dahall/Vanara/blob/master/PInvoke/Shell32/Clipboard.cs
    /// <summary>An array of flags that indicate which of the <see cref="FILEDESCRIPTOR"/> structure members contain valid data.</summary>
    [Flags]
    public enum FD_FLAGS : uint
    {
        /// <summary>The <c>clsid</c> member is valid.</summary>
        FD_CLSID = 0x00000001,

        /// <summary>The <c>sizel</c> and <c>pointl</c> members are valid.</summary>
        FD_SIZEPOINT = 0x00000002,

        /// <summary>The <c>dwFileAttributes</c> member is valid.</summary>
        FD_ATTRIBUTES = 0x00000004,

        /// <summary>The <c>ftCreationTime</c> member is valid.</summary>
        FD_CREATETIME = 0x00000008,

        /// <summary>The <c>ftLastAccessTime</c> member is valid.</summary>
        FD_ACCESSTIME = 0x00000010,

        /// <summary>The <c>ftLastWriteTime</c> member is valid.</summary>
        FD_WRITESTIME = 0x00000020,

        /// <summary>The <c>nFileSizeHigh</c> and <c>nFileSizeLow</c> members are valid.</summary>
        FD_FILESIZE = 0x00000040,

        /// <summary>A progress indicator is shown with drag-and-drop operations.</summary>
        FD_PROGRESSUI = 0x00004000,

        /// <summary>Treat the operation as a shortcut.</summary>
        FD_LINKUI = 0x00008000,

        /// <summary><c>Windows Vista and later</c>. The descriptor is Unicode.</summary>
        FD_UNICODE = 0x80000000,
    }

    // https://github.com/dahall/Vanara/blob/master/PInvoke/Shared/WinNT/FileFlagsAndAttributes.cs
    /// <summary>
    /// File attributes are metadata values stored by the file system on disk and are used by the system and are available to developers via
    /// various file I/O APIs.
    /// </summary>
    [Flags]
    public enum FileFlagsAndAttributes : uint
    {
        /// <summary>
        /// A file that is read-only. Applications can read the file, but cannot write to it or delete it. This attribute is not honored on
        /// directories.
        /// </summary>
        FILE_ATTRIBUTE_READONLY = 0x00000001,

        /// <summary>The file or directory is hidden. It is not included in an ordinary directory listing.</summary>
        FILE_ATTRIBUTE_HIDDEN = 0x00000002,

        /// <summary>A file or directory that the operating system uses a part of, or uses exclusively.</summary>
        FILE_ATTRIBUTE_SYSTEM = 0x00000004,

        /// <summary>The handle that identifies a directory.</summary>
        FILE_ATTRIBUTE_DIRECTORY = 0x00000010,

        /// <summary>
        /// A file or directory that is an archive file or directory. Applications typically use this attribute to mark files for backup or
        /// removal.
        /// </summary>
        FILE_ATTRIBUTE_ARCHIVE = 0x00000020,

        /// <summary>This value is reserved for system use.</summary>
        FILE_ATTRIBUTE_DEVICE = 0x00000040,

        /// <summary>A file that does not have other attributes set. This attribute is valid only when used alone.</summary>
        FILE_ATTRIBUTE_NORMAL = 0x00000080,

        /// <summary>
        /// A file that is being used for temporary storage. File systems avoid writing data back to mass storage if sufficient cache memory is
        /// available, because typically, an application deletes a temporary file after the handle is closed. In that scenario, the system can
        /// entirely avoid writing the data. Otherwise, the data is written after the handle is closed.
        /// </summary>
        FILE_ATTRIBUTE_TEMPORARY = 0x00000100,

        // The following flags are omitted
    }

    private enum MonitorType
    {
        Primary = 0x00000001,
        Nearest = 0x00000002,
    }

    #endregion

    #region General Use Structs

    /// <summary>
    /// An integer version of <see cref="Point"/>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public Int32 X;
        public Int32 Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static implicit operator Point(POINT self)
            => new(self.X, self.Y);

        public static implicit operator System.Windows.Point(POINT self)
            => new(self.X, self.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public Int32 Width;
        public Int32 Height;

        public static implicit operator Size(SIZE self)
            => new(self.Width, self.Height);
    }

    #endregion

    #region Icon

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFO
    {
        public HANDLE hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    };

    [DllImport("Shell32.dll")]
    private static extern HANDLE SHGetFileInfo(
        string pszPath, FileFlagsAndAttributes dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, FileInfoFlags uFlags);

    [DllImport("User32.dll")]
    private static extern int DestroyIcon(HANDLE hIcon);

    public static int GetIconIndex(string fileName, FileFlagsAndAttributes dwAttr, FileInfoFlags dwFlags, FileInfoFlags iconState)
    {
        SHFILEINFO shfi = new();
        var retVal = SHGetFileInfo(
            fileName,
            dwAttr,
            ref shfi,
            (uint)Marshal.SizeOf(shfi),
            dwFlags | iconState);

        return retVal == IntPtr.Zero ? 0 : shfi.iIcon;
    }

    public static Icon GetIcon(string filePath, FileInfoFlags flags)
    {
        SHFILEINFO shinfo = new();
        _ = SHGetFileInfo(
            filePath,
            FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
            ref shinfo,
            (uint)Marshal.SizeOf(shinfo),
            FileInfoFlags.SHGFI_ICON | flags);

        Icon icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
        _ = DestroyIcon(shinfo.hIcon);

        return icon;
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out HANDLE phiconLarge, HANDLE phiconSmall, int nIcons);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, HANDLE phiconLarge, out HANDLE phiconSmall, int nIcons);

    public static Icon ExtractIconByIndex(string filePath, int index, FileToIconConverter.IconSize iconSize)
    {
        HANDLE hIcon;
        if (iconSize == FileToIconConverter.IconSize.Large)
        {
            _ = ExtractIconEx(filePath, index, out hIcon, IntPtr.Zero, 1);
        }
        else
        {
            _ = ExtractIconEx(filePath, index, IntPtr.Zero, out hIcon, 1);
        }

        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        _ = DestroyIcon(hIcon);

        return icon;
    }

    [DllImport("Gdi32.dll")]
    private static extern bool DeleteObject(HANDLE hObject);

    public static void MDeleteObject(HANDLE hObject)
        => DeleteObject(hObject);

    #endregion

    #region File Name & Type

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static int StringCompareLogical(string a, string b) => StrCmpLogicalW(a, b);

    public static string GetShellFileType(string fileName)
    {
        SHFILEINFO shInfo = new();
        const FileInfoFlags flags = FileInfoFlags.SHGFI_TYPENAME | FileInfoFlags.SHGFI_USEFILEATTRIBUTES;

        var fileInfo = SHGetFileInfo(fileName,
                              FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                              ref shInfo,
                              (uint)Marshal.SizeOf(shInfo),
                              flags);

        if (fileInfo == IntPtr.Zero)
            return "File";

        return shInfo.szTypeName;
    }

    #endregion

    #region Monitor Info

    [DllImport("User32.dll")]
    private static extern HANDLE MonitorFromWindow(HANDLE handle, MonitorType flags);

    public static HANDLE NearestMonitor(HANDLE handle) => MonitorFromWindow(handle, MonitorType.Nearest);

    public static HANDLE PrimaryMonitor() => MonitorFromWindow(IntPtr.Zero, MonitorType.Primary);

    [DllImport("User32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public static System.Windows.Point GetCursorPos()
    {
        GetCursorPos(out var lpPoint);

        return lpPoint;
    }

    #endregion

    #region Process Info

    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("Kernel32.dll")]
    private static extern bool GetProcessIoCounters(HANDLE ProcessHandle, out IO_COUNTERS IoCounters);

    public static IO_COUNTERS GetProcessIoCounters(HANDLE ProcessHandle)
    {
        GetProcessIoCounters(ProcessHandle, out var counters);

        return counters;
    }

    #endregion

    /// <summary>
    /// Returns the in-memory representation of an interop structure.
    /// </summary>
    /// <param name="source">Structure to return.</param>
    /// <returns>In-memory representation of structure.</returns>
    private static IEnumerable<byte> BytesFromStructure<T>(T source)
    {
        // Set up for call to StructureToPtr
        var size = Marshal.SizeOf(source.GetType());
        var ptr = Marshal.AllocHGlobal(size);
        var bytes = new byte[size];
        try
        {
            Marshal.StructureToPtr(source, ptr, false);
            // Copy marshalled bytes to buffer
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return bytes;
    }

    private static T StructureFromBytes<T>(IEnumerable<byte> bytes)
    {
        var handle = GCHandle.Alloc(bytes.ToArray(), GCHandleType.Pinned);

        var data = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());

        handle.Free();

        return data;
    }

}

/// <summary>
/// Definition of the IAsyncOperation COM interface.
/// </summary>
/// <remarks>
/// Pseudo-public because VirtualFileDataObject implements it.
/// </remarks>
[ComImport]
[Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAsyncOperation
{
    void SetAsyncMode([In] Int32 fDoOpAsync);
    void GetAsyncMode([Out] out Int32 pfIsOpAsync);
    void StartOperation([In] IBindCtx pbcReserved);
    void InOperation([Out] out Int32 pfInAsyncOp);
    void EndOperation([In] Int32 hResult, [In] IBindCtx pbcReserved, [In] UInt32 dwEffects);
}
