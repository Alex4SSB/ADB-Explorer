namespace ADB_Explorer.Services;

using Helpers;
using System.Drawing;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    #region Constants

    public const int MAX_PATH = 260;

    public const int VARIANT_FALSE = 0;
    public const int VARIANT_TRUE = -1;

    #endregion

    #region General Use Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public long dwDateTime;

        public FILETIME(DateTime dateUTC)
        {
            dwDateTime = dateUTC.ToLocalTime().ToFileTime();
        }

        public FILETIME(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
        {
            dwDateTime = ((long)fileTime.dwHighDateTime << 32) + fileTime.dwLowDateTime;
        }

        public readonly DateTime DateTimeUTC => DateTime.FromFileTime(dwDateTime).ToUniversalTime();

        // Another app's FILETIME may hold anything, which DateTime can't always represent
        public readonly DateTime? SafeDateTimeUTC
            => dwDateTime >= 0 && dwDateTime <= MaxFileTime ? DateTime.FromFileTimeUtc(dwDateTime) : null;

        private static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

        public readonly DateTime DateTimeLocal => DateTime.FromFileTime(dwDateTime).ToLocalTime();

        public override readonly string ToString() => DateTimeUTC.ToString(CultureInfo.CurrentCulture);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILESIZE(long size)
    {
        // 64-bit Int as 2 swapped Little Endian DWORDs
        // So as a whole, cannot be treated neither as LE nor BE

        public UInt32 nFileSizeHigh = (uint)(size >> 32);
        public UInt32 nFileSizeLow = (uint)(size & 0xffffffff);

        public readonly long GetSize() => ((long)nFileSizeHigh << 32) | nFileSizeLow;

        public override readonly string ToString() => UnitFormatter.BytesToSize(GetSize());
    }

    #endregion

    #region Icon

    public static int GetIconIndex(string fileName, FileFlagsAndAttributes dwAttr, SHGFI dwFlags, SHGFI iconState)
    {
        SHFILEINFO shfi = new();
        var retVal = SHGetFileInfo(
            fileName,
            (FileAttributes)dwAttr,
            ref shfi,
            SHFILEINFO.Size,
            dwFlags | iconState);

        return retVal == IntPtr.Zero ? 0 : shfi.iIcon;
    }

    public static Icon GetIcon(string filePath, SHGFI flags)
    {
        SHFILEINFO shinfo = new();
        SHGetFileInfo(
            filePath,
            FileAttributes.Normal,
            ref shinfo,
            SHFILEINFO.Size,
            SHGFI.SHGFI_ICON | flags);

        if (shinfo.hIcon.IsNull)
            return null;

        Icon icon = (Icon)Icon.FromHandle((IntPtr)shinfo.hIcon).Clone();
        DestroyIcon(shinfo.hIcon);

        return icon;
    }

    public static Icon ExtractIconByIndex(string filePath, int index, FileIconProvider.IconSize iconSize)
    {
        var icons = new HICON[1];
        if (iconSize == FileIconProvider.IconSize.Large)
        {
            ExtractIconEx(filePath, index, icons, null, 1);
        }
        else
        {
            ExtractIconEx(filePath, index, null, icons, 1);
        }

        var hIcon = icons[0];
        Icon icon = (Icon)Icon.FromHandle((IntPtr)hIcon).Clone();
        DestroyIcon(hIcon);

        return icon;
    }

    // Vanara's overload takes SafeHICON[], which the marshaller rejects (arrays of SafeHandles)
    [DllImport("User32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        out HICON phicon,
        out int piconid,
        uint nIcons,
        uint flags);

    public static Icon ExtractIconByIndex(string filePath, int index, int size)
    {
        uint result = PrivateExtractIcons(filePath, index, size, size, out var hIcon, out _, 1, 0);
        if (result == 0 || hIcon.IsNull)
            return null;

        Icon icon = (Icon)Icon.FromHandle((IntPtr)hIcon).Clone();
        DestroyIcon(hIcon);

        return icon;
    }

    #endregion

    #region File Name & Type

    public static string GetShellFileType(string fileName)
    {
        SHFILEINFO shInfo = new();
        const SHGFI flags = SHGFI.SHGFI_TYPENAME | SHGFI.SHGFI_USEFILEATTRIBUTES;

        var fileInfo = SHGetFileInfo(fileName,
                              FileAttributes.Normal,
                              ref shInfo,
                              SHFILEINFO.Size,
                              flags);

        if (fileInfo == IntPtr.Zero)
            return "File";

        return shInfo.szTypeName;
    }

    #endregion

    public static void RefreshExplorerDirectory(string directoryPath)
    {
        var hDir = (nuint)Marshal.StringToHGlobalUni(directoryPath);

        try
        {
            SHChangeNotify(SHCNE.SHCNE_UPDATEDIR, SHCNF.SHCNF_PATHW, hDir);
        }
        finally
        {
            Marshal.FreeHGlobal((nint)hDir);
        }
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

    private const int PACKAGE_FAMILY_NAME_MAX_LENGTH = 64;

    private static readonly Lazy<string?> PackageFamilyName = new(ReadPackageFamilyName);

    public static string? GetCurrentPackageFamilyName() => PackageFamilyName.Value;

    private static string? ReadPackageFamilyName()
    {
        var builder = new StringBuilder(PACKAGE_FAMILY_NAME_MAX_LENGTH + 1);
        uint length = (uint)builder.Capacity;
        return Kernel32.GetCurrentPackageFamilyName(ref length, builder).Succeeded ? builder.ToString() : null;
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
