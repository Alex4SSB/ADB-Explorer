namespace ADB_Explorer.Services;

using Helpers;
using System.Drawing;
using System.Runtime.InteropServices.ComTypes;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    #region Constants

    public const int MAX_PATH = 260;

    public const int VARIANT_FALSE = 0;
    public const int VARIANT_TRUE = -1;

    #endregion

    #region Enums

    public enum HResult : int
    {
        /// <summary>S_OK</summary>
        Ok = 0x0000,

        /// <summary>S_FALSE</summary>
        False = 0x0001,

        /// <summary>E_INVALIDARG</summary>
        InvalidArguments = -2147024809, // 0x80070057

        /// <summary>E_OUTOFMEMORY</summary>
        OutOfMemory = -2147024882, // 0x8007000E

        /// <summary>E_NOINTERFACE</summary>
        NoInterface = -2147467262, // 0x80004002

        /// <summary>E_FAIL</summary>
        Fail = -2147467259, // 0x80004005

        /// <summary>E_ELEMENTNOTFOUND</summary>
        ElementNotFound = -2147023728, // 0x80070490

        /// <summary>TYPE_E_ELEMENTNOTFOUND</summary>
        TypeElementNotFound = -2147319765, // 0x8002802B

        /// <summary>NO_OBJECT</summary>
        NoObject = -2147221019, // 0x800401E5

        /// <summary>Win32 Error code: ERROR_CANCELLED</summary>
        Win32ErrorCanceled = 1223,

        /// <summary>ERROR_CANCELLED</summary>
        Canceled = -2147023673, // 0x800704C7

        /// <summary>The requested resource is in use</summary>
        ResourceInUse = -2147024726, // 0x800700AA

        /// <summary>The requested resources is read-only.</summary>
        AccessDenied = -2147287035, // 0x80030005

        /// <summary>VS specific error HRESULT for "Unsupported format".</summary>
        OLE_E_ADVISENOTSUPPORTED = -2147221501, // 0x80040003

        /// <summary>Invalid FORMATETC structure</summary>
        DV_E_FORMATETC = -2147221404, // 0x80040064

        /// <summary>Invalid aspect(s)</summary>
        DV_E_DVASPECT = -2147221397, // 0x8004006B

        /// <summary>Invalid tymed</summary>
        DV_E_TYMED = -2147221399, // 0x80040069

        /// <summary>The OLE drag-and-drop operation was successful.</summary>
        DRAGDROP_S_DROP = 0x00040100,

        /// <summary>The OLE drag-and-drop operation was canceled.</summary>
        DRAGDROP_S_CANCEL = 0x00040101,

        /// <summary>Indicates successful completion of the method, and requests OLE to update the cursor using the OLE-provided default cursors.</summary>
        DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102,

        /// <summary>Catastrophic failure</summary>
        E_UNEXPECTED = -2147418113, // 0x8000FFFF
    }

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

        /// <summary>A file that is a sparse file.</summary>
        FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,

        /// <summary>A file or directory that has an associated reparse point, or a file that is a symbolic link.</summary>
        FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,

        /// <summary>
        /// A file or directory that is compressed. For a file, all of the data in the file is compressed. For a directory, compression is the
        /// default for newly created files and subdirectories.
        /// </summary>
        FILE_ATTRIBUTE_COMPRESSED = 0x00000800,

        /// <summary>
        /// The data of a file is not available immediately. This attribute indicates that the file data is physically moved to offline storage.
        /// This attribute is used by Remote Storage, which is the hierarchical storage management software. Applications should not arbitrarily
        /// change this attribute.
        /// </summary>
        FILE_ATTRIBUTE_OFFLINE = 0x00001000,

        /// <summary>The file or directory is not to be indexed by the content indexing service.</summary>
        FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,

        /// <summary>
        /// A file or directory that is encrypted. For a file, all data streams in the file are encrypted. For a directory, encryption is the
        /// default for newly created files and subdirectories.
        /// </summary>
        FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,

        /// <summary>
        /// The directory or user data stream is configured with integrity (only supported on ReFS volumes). It is not included in an ordinary
        /// directory listing. The integrity setting persists with the file if it's renamed. If a file is copied the destination file will have
        /// integrity set if either the source file or destination directory have integrity set.
        /// <para>
        /// <c>Windows Server 2008 R2, Windows 7, Windows Server 2008, Windows Vista, Windows Server 2003 and Windows XP:</c> This flag is not
        /// supported until Windows Server 2012.
        /// </para>
        /// </summary>
        FILE_ATTRIBUTE_INTEGRITY_STREAM = 0x00008000,

        /// <summary>This value is reserved for system use.</summary>
        FILE_ATTRIBUTE_VIRTUAL = 0x00010000,

        /// <summary>
        /// The user data stream not to be read by the background data integrity scanner (AKA scrubber). When set on a directory it only provides
        /// inheritance. This flag is only supported on Storage Spaces and ReFS volumes. It is not included in an ordinary directory listing.
        /// <para>
        /// <c>Windows Server 2008 R2, Windows 7, Windows Server 2008, Windows Vista, Windows Server 2003 and Windows XP:</c> This flag is not
        /// supported until Windows 8 and Windows Server 2012.
        /// </para>
        /// </summary>
        FILE_ATTRIBUTE_NO_SCRUB_DATA = 0x00020000,

        /// <summary>
        /// This attribute only appears in directory enumeration classes (FILE_DIRECTORY_INFORMATION, FILE_BOTH_DIR_INFORMATION, etc.). When this
        /// attribute is set, it means that the file or directory has no physical representation on the local system; the item is virtual.
        /// Opening the item will be more expensive than normal, e.g. it will cause at least some of it to be fetched from a remote store.
        /// </summary>
        FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000,

        /// <summary>Used to prevent the file from being purged from local storage when running low on disk space.</summary>
        FILE_ATTRIBUTE_PINNED = 0x00080000,

        /// <summary>Indicate that the file is not stored locally.</summary>
        FILE_ATTRIBUTE_UNPINNED = 0x00100000,

        /// <summary>
        /// When this attribute is set, it means that the file or directory is not fully present locally. For a file that means that not all of
        /// its data is on local storage (e.g. it may be sparse with some data still in remote storage). For a directory it means that some of
        /// the directory contents are being virtualized from another location. Reading the file / enumerating the directory will be more
        /// expensive than normal, e.g. it will cause at least some of the file/directory content to be fetched from a remote store. Only
        /// kernel-mode callers can set this bit.
        /// </summary>
        FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000,

        /// <summary>
        /// The file or device is being opened with no system caching for data reads and writes. This flag does not affect hard disk caching or
        /// memory mapped files.
        /// <para>
        /// There are strict requirements for successfully working with files opened with CreateFile using the FILE_FLAG_NO_BUFFERING flag, for
        /// details see File Buffering.
        /// </para>
        /// </summary>
        FILE_FLAG_NO_BUFFERING = 0x20000000,

        /// <summary>
        /// Write operations will not go through any intermediate cache, they will go directly to disk.
        /// <para>For additional information, see the Caching Behavior section of this topic.</para>
        /// </summary>
        FILE_FLAG_WRITE_THROUGH = 0x80000000,

        /// <summary>
        /// The file or device is being opened or created for asynchronous I/O.
        /// <para>
        /// When subsequent I/O operations are completed on this handle, the event specified in the OVERLAPPED structure will be set to the
        /// signaled state.
        /// </para>
        /// <para>If this flag is specified, the file can be used for simultaneous read and write operations.</para>
        /// <para>
        /// If this flag is not specified, then I/O operations are serialized, even if the calls to the read and write functions specify an
        /// OVERLAPPED structure.
        /// </para>
        /// <para>
        /// For information about considerations when using a file handle created with this flag, see the Synchronous and Asynchronous I/O
        /// Handles section of this topic.
        /// </para>
        /// </summary>
        FILE_FLAG_OVERLAPPED = 0x40000000,

        /// <summary>
        /// Access is intended to be random. The system can use this as a hint to optimize file caching.
        /// <para>This flag has no effect if the file system does not support cached I/O and FILE_FLAG_NO_BUFFERING.</para>
        /// <para>For more information, see the Caching Behavior section of this topic.</para>
        /// </summary>
        FILE_FLAG_RANDOM_ACCESS = 0x10000000,

        /// <summary>
        /// Access is intended to be sequential from beginning to end. The system can use this as a hint to optimize file caching.
        /// <para>This flag should not be used if read-behind (that is, reverse scans) will be used.</para>
        /// <para>This flag has no effect if the file system does not support cached I/O and FILE_FLAG_NO_BUFFERING.</para>
        /// <para>For more information, see the Caching Behavior section of this topic.</para>
        /// </summary>
        FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000,

        /// <summary>
        /// The file is to be deleted immediately after all of its handles are closed, which includes the specified handle and any other open or
        /// duplicated handles.
        /// <para>If there are existing open handles to a file, the call fails unless they were all opened with the FILE_SHARE_DELETE share mode.</para>
        /// <para>Subsequent open requests for the file fail, unless the FILE_SHARE_DELETE share mode is specified.</para>
        /// </summary>
        FILE_FLAG_DELETE_ON_CLOSE = 0x04000000,

        /// <summary>
        /// The file is being opened or created for a backup or restore operation. The system ensures that the calling process overrides file
        /// security checks when the process has SE_BACKUP_NAME and SE_RESTORE_NAME privileges. For more information, see Changing Privileges in
        /// a Token.
        /// <para>
        /// You must set this flag to obtain a handle to a directory. A directory handle can be passed to some functions instead of a file
        /// handle. For more information, see the Remarks section.
        /// </para>
        /// </summary>
        FILE_FLAG_BACKUP_SEMANTICS = 0x02000000,

        /// <summary>
        /// Access will occur according to POSIX rules. This includes allowing multiple files with names, differing only in case, for file
        /// systems that support that naming. Use care when using this option, because files created with this flag may not be accessible by
        /// applications that are written for MS-DOS or 16-bit Windows.
        /// </summary>
        FILE_FLAG_POSIX_SEMANTICS = 0x01000000,

        /// <summary>
        /// The file or device is being opened with session awareness. If this flag is not specified, then per-session devices (such as a device
        /// using RemoteFX USB Redirection) cannot be opened by processes running in session 0. This flag has no effect for callers not in
        /// session 0. This flag is supported only on server editions of Windows.
        /// <para><c>Windows Server 2008 R2 and Windows Server 2008:</c> This flag is not supported before Windows Server 2012.</para>
        /// </summary>
        FILE_FLAG_SESSION_AWARE = 0x00800000,

        /// <summary>
        /// Normal reparse point processing will not occur; CreateFile will attempt to open the reparse point. When a file is opened, a file
        /// handle is returned, whether or not the filter that controls the reparse point is operational.
        /// <para>This flag cannot be used with the CREATE_ALWAYS flag.</para>
        /// <para>If the file is not a reparse point, then this flag is ignored.</para>
        /// </summary>
        FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000,

        // Duplicate flags have been omitted
    }

    private enum WinHooks
    {
        WH_MSGFILTER = -1,
        WH_JOURNALRECORD = 0,
        WH_JOURNALPLAYBACK = 1,
        WH_KEYBOARD = 2,
        WH_GETMESSAGE = 3,
        WH_CALLWNDPROC = 4,
        WH_CBT = 5,
        WH_SYSMSGFILTER = 6,
        WH_MOUSE = 7,
        WH_DEBUG = 9,
        WH_SHELL = 10,
        WH_FOREGROUNDIDLE = 11,
        WH_CALLWNDPROCRET = 12,
        WH_KEYBOARD_LL = 13,
        WH_MOUSE_LL = 14,
    }

    public enum ClipboardNotificationMessage
    {
        WM_ASKCBFORMATNAME = 0x030C,
        WM_CHANGECBCHAIN = 0x030D,
        WM_CLIPBOARDUPDATE = 0x031D,
        WM_DESTROYCLIPBOARD = 0x0307,
        WM_DRAWCLIPBOARD = 0x0308,
        WM_HSCROLLCLIPBOARD = 0x030E,
        WM_PAINTCLIPBOARD = 0x0309,
        WM_RENDERALLFORMATS = 0x0306,
        WM_RENDERFORMAT = 0x0305,
        WM_SIZECLIPBOARD = 0x030B,
        WM_VSCROLLCLIPBOARD = 0x030A,
    }

    public enum MouseMessages
    {
        WM_LBUTTONDOWN = 0x0201,
        WM_LBUTTONUP = 0x0202,
        WM_MOUSEMOVE = 0x0200,
        WM_MOUSEWHEEL = 0x020A,
        WM_RBUTTONDOWN = 0x0204,
        WM_RBUTTONUP = 0x0205
    }

    public enum WindowsMessages
    {
        WM_COPYDATA = 0x004A,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public HANDLE dwData;
        public int cbData;
        [MarshalAs(UnmanagedType.LPStr)]
        public string lpData;
    }

    #endregion

    #region General Use Structs

    /// <summary>
    /// An integer version of <see cref="Point"/>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT(int x, int y)
    {
        public Int32 X = x;
        public Int32 Y = y;

        public static implicit operator Point(POINT self)
            => new(self.X, self.Y);

        public static implicit operator POINT(System.Windows.Point self)
            => new((int)self.X, (int)self.Y);

        public static implicit operator System.Windows.Point(POINT self)
            => new(self.X, self.Y);

        public static bool operator == (POINT self, POINT other)
            => self.X == other.X && self.Y == other.Y;

        public static bool operator !=(POINT self, POINT other)
            => self.X != other.X || self.Y != other.Y;

        public readonly override bool Equals(object obj)
            => obj is POINT other &&
                X == other.X &&
                Y == other.Y;

        public readonly override int GetHashCode()
            => HashCode.Combine(X, Y);

        public readonly override string ToString()
            => $"{X},{Y}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public Int32 Width;
        public Int32 Height;

        public static implicit operator Size(SIZE self)
            => new(self.Width, self.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public override string ToString()
            => $"{Left},{Top},{Right},{Bottom}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME(DateTime dateUTC)
    {
        public long dwDateTime = dateUTC.ToLocalTime().ToFileTime();

        public readonly DateTime DateTimeUTC => DateTime.FromFileTime(dwDateTime).ToUniversalTime();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILESIZE(long size)
    {
        // 64-bit Int as 2 swapped Little Endian DWORDs
        // So as a whole, cannot be treated neither as LE nor BE

        public UInt32 nFileSizeHigh = (uint)(size >> 32);
        public UInt32 nFileSizeLow = (uint)(size & 0xffffffff);

        public readonly long GetSize() => ((long)nFileSizeHigh << 32) | nFileSizeLow;
    }

    #endregion

    #region Icon

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFO
    {
        public HANDLE hIcon;
        public int iIcon;
        public FileFlagsAndAttributes dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    };

    [DllImport("Shell32.dll")]
    private static extern HANDLE SHGetFileInfo(
        string pszPath, FileFlagsAndAttributes dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, FileInfoFlags uFlags);

    [DllImport("User32.dll")]
    private static extern HResult DestroyIcon(HANDLE hIcon);

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
        SHGetFileInfo(
            filePath,
            FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
            ref shinfo,
            (uint)Marshal.SizeOf(shinfo),
            FileInfoFlags.SHGFI_ICON | flags);

        if (shinfo.hIcon == IntPtr.Zero)
            return null;

        Icon icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
        DestroyIcon(shinfo.hIcon);

        return icon;
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern HResult ExtractIconEx(string lpszFile, int nIconIndex, out HANDLE phiconLarge, HANDLE phiconSmall, int nIcons);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern HResult ExtractIconEx(string lpszFile, int nIconIndex, HANDLE phiconLarge, out HANDLE phiconSmall, int nIcons);

    public static Icon ExtractIconByIndex(string filePath, int index, FileToIconConverter.IconSize iconSize)
    {
        HANDLE hIcon;
        if (iconSize == FileToIconConverter.IconSize.Large)
        {
            ExtractIconEx(filePath, index, out hIcon, IntPtr.Zero, 1);
        }
        else
        {
            ExtractIconEx(filePath, index, IntPtr.Zero, out hIcon, 1);
        }

        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);

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

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(HANDLE hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    public static bool SendMessage(HANDLE windowHandle, WindowsMessages messageType, ref COPYDATASTRUCT data)
    {
        var result = SendMessage(windowHandle, (uint)messageType, IntPtr.Zero, ref data);
        return result != IntPtr.Zero;
    }

    /// <summary>
    /// Returns the in-memory representation of an interop structure.
    /// </summary>
    /// <param name="source">Structure to return.</param>
    /// <returns>In-memory representation of structure.</returns>
    public static byte[] BytesFromStructure<T>(T source)
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

    public static T StructureFromBytes<T>(IEnumerable<byte> bytes)
    {
        var handle = GCHandle.Alloc(bytes.ToArray(), GCHandleType.Pinned);

        var data = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());

        handle.Free();

        return data;
    }

    public static void ThrowExceptionForHR(HResult hr)
        => Marshal.ThrowExceptionForHR((int)hr);
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
