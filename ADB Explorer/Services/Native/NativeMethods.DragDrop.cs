namespace ADB_Explorer.Services;

using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    [GeneratedComInterface]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public partial interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int fEscapePressed, uint grfKeyState);
        [PreserveSig]
        int GiveFeedback(uint dwEffect);
    }

    [DllImport("Shell32.dll")]
    private static extern HResult SHCreateStdEnumFmtEtc(uint cfmt, FORMATETC[] afmt, out IEnumFORMATETC ppenumFormatEtc);

    public static HResult SHCreateStdEnumFmtEtc(int cfmt, IEnumerable<FORMATETC> afmt, out IEnumFORMATETC ppenumFormatEtc)
        => SHCreateStdEnumFmtEtc((uint)cfmt, [.. afmt], out ppenumFormatEtc);

    [return: MarshalAs(UnmanagedType.Interface)]
    [DllImport("Ole32.dll", PreserveSig = false)]
    private static extern IStream CreateStreamOnHGlobal(HANDLE hGlobal, [MarshalAs(UnmanagedType.Bool)] bool fDeleteOnRelease);

    public static IStream MCreateStreamOnHGlobal(HANDLE hGlobal, bool fDeleteOnRelease)
        => CreateStreamOnHGlobal(hGlobal, fDeleteOnRelease);

    [DllImport("Ole32.dll", CharSet = CharSet.Auto, ExactSpelling = true, PreserveSig = false)]
    private static extern HResult DoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects, out DragDropEffects finalEffect);

    public static DragDropEffects MDoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects)
    {
        DoDragDrop(dataObject, dropSource, allowedEffects, out var finalEffect);

        return finalEffect;
    }

    [LibraryImport("Kernel32.dll")]
    private static partial HANDLE GlobalLock(HANDLE hMem);

    public static HANDLE MGlobalLock(HANDLE hMem)
        => GlobalLock(hMem);

    [return: MarshalAs(UnmanagedType.Bool)]
    [LibraryImport("Kernel32.dll")]
    private static partial bool GlobalUnlock(HANDLE hMem);

    public static void MGlobalUnlock(HANDLE hMem)
        => GlobalUnlock(hMem);

    [LibraryImport("Kernel32.dll")]
    private static partial HANDLE GlobalSize(HANDLE handle);

    public static HANDLE MGlobalSize(HANDLE handle)
        => GlobalSize(handle);

    public interface IByteStruct
    {
        public IEnumerable<byte> Bytes { get; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ADBDRAGLIST : IByteStruct
    {
        public string deviceId;
        public string parentFolder;
        public string[] items;

        public ADBDRAGLIST(LogicalDeviceViewModel device, IEnumerable<FileClass> files)
        {
            deviceId = device.ID;
            parentFolder = files.First().ParentPath;
            
            items = [.. parentFolder == AdbExplorerConst.RECYCLE_PATH
                ? files.Select(f => f.TrashIndex!.RecycleName)
                : files.Select(f => f.FullName)];
        }

        public readonly IEnumerable<byte> Bytes
        {
            get
            {
                var joinedItems = string.Join("\0", items);
                var combined = string.Join("\0", deviceId, parentFolder, joinedItems, '\0');

                return [.. Encoding.Unicode.GetBytes(combined)];
            }
        }

        public static ADBDRAGLIST FromStream(MemoryStream stream)
        {
            ADBDRAGLIST dragList = new();
            var bytes = stream.ToArray().AsSpan();

            int i = 0;
            List<string> strings = [];

            while (i < bytes.Length)
            {
                // Index of Unicode chars must be even
                var index = ByteHelper.PatternAt(bytes, [0, 0], i, true);

                if (index < 0)
                    break;

                string item = Encoding.Unicode.GetString(bytes[i..index]);
                if (string.IsNullOrEmpty(item) || bytes[i..index].Sum() == 0)
                    break;

                strings.Add(item);

                i = index + 2;
            }

            dragList.deviceId = strings[0];
            dragList.parentFolder = strings[1];
            dragList.items = [.. strings.Skip(2)];

            return dragList;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFHDROP : IByteStruct
    {
        public DROPFILES dropFiles;
        public string[] fileNames;

        public IEnumerable<byte> Bytes
        {
            get
            {
                List<byte> bytes = [];

                dropFiles.pFiles = (uint)Marshal.SizeOf<DROPFILES>();
                dropFiles.fWide = true;

                bytes.AddRange(BytesFromStructure(dropFiles));

                bytes.AddRange(Encoding.Unicode.GetBytes(string.Join('\0', fileNames) + "\0\0"));

                return bytes;
            }
        }

        public CFHDROP(IEnumerable<string> fileNames)
        {
            this.fileNames = [.. fileNames];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DROPFILES
    {
        public uint pFiles; // Offset to the file list
        public POINT pt;    // Drop point (usually (0, 0))
        public bool fNC;    // Non-client area flag (usually FALSE)
        public bool fWide;  // Unicode flag (usually TRUE)
    }

    /// <summary>
    /// IStream with its buffers as pointers, so a stream we serve doesn't get a managed array
    /// allocated and copied for every Read the shell makes.
    /// </summary>
    [ComImport]
    [Guid("0000000c-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPointerStream
    {
        [PreserveSig] HResult Read(HANDLE pv, int cb, HANDLE pcbRead);
        [PreserveSig] HResult Write(HANDLE pv, int cb, HANDLE pcbWritten);
        [PreserveSig] HResult Seek(long dlibMove, SeekOrigin dwOrigin, HANDLE plibNewPosition);
        [PreserveSig] HResult SetSize(long libNewSize);
        [PreserveSig] HResult CopyTo(HANDLE pstm, long cb, HANDLE pcbRead, HANDLE pcbWritten);
        [PreserveSig] HResult Commit(int grfCommitFlags);
        [PreserveSig] HResult Revert();
        [PreserveSig] HResult LockRegion(long libOffset, long cb, int dwLockType);
        [PreserveSig] HResult UnlockRegion(long libOffset, long cb, int dwLockType);
        [PreserveSig] HResult Stat(out STATSTG pstatstg, int grfStatFlag);
        [PreserveSig] HResult Clone(out HANDLE ppstm);
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(HANDLE hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("Ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    /// <summary>
    /// A stream over a copy of <paramref name="medium"/>'s memory, which is then released.
    /// Copied, as the source may own that memory (pUnkForRelease).
    /// </summary>
    public static IStream TakeHGlobalAsStream(ref STGMEDIUM medium)
    {
        byte[] content;
        try
        {
            var size = (int)GlobalSize(medium.unionmember);
            var data = GlobalLock(medium.unionmember);
            content = new byte[size];

            if (data != 0)
            {
                Marshal.Copy(data, content, 0, size);
                GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }

        var stream = CreateStreamOnHGlobal(0, true);
        stream.Write(content, content.Length, 0);
        stream.Seek(0, 0, 0);

        return stream;
    }

    /// <summary>The paths in a data object's CF_HDROP, read from the source's own COM object.</summary>
    public static string[] GetFileDropPaths(IDataObject dataObject)
    {
        var format = new FORMATETC
        {
            cfFormat = AdbDataFormats.FileDrop,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        dataObject.GetData(ref format, out var medium);
        try
        {
            var count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
            var paths = new string[count];
            for (uint i = 0; i < count; i++)
            {
                var path = new StringBuilder((int)DragQueryFile(medium.unionmember, i, null, 0) + 1);
                DragQueryFile(medium.unionmember, i, path, (uint)path.Capacity);
                paths[i] = path.ToString();
            }

            return paths;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    [DllImport("Ole32.dll", PreserveSig = false)]
    private static extern HANDLE CoMarshalInterThreadInterfaceInStream(in Guid riid, [MarshalAs(UnmanagedType.Interface)] object pUnk);

    [DllImport("Ole32.dll", PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.Interface)]
    private static extern object CoGetInterfaceAndReleaseStream(HANDLE pStm, in Guid iid);

    /// <summary>
    /// Hands a COM object to another thread: <paramref name="dataObject"/> is marshaled here, and the returned
    /// function unmarshals it on the thread that calls it (once), so its calls don't go through this one.
    /// </summary>
    public static Func<IDataObject> MarshalToThread(IDataObject dataObject)
    {
        var iid = typeof(IDataObject).GUID;
        var stream = CoMarshalInterThreadInterfaceInStream(iid, dataObject);

        return () => (IDataObject)CoGetInterfaceAndReleaseStream(stream, iid);
    }

    [LibraryImport("User32.dll")]
    private static partial HANDLE GetForegroundWindow();

    [LibraryImport("User32.dll")]
    private static partial uint GetWindowThreadProcessId(HANDLE hWnd, out uint lpdwProcessId);

    [LibraryImport("User32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, HANDLE dwExtraInfo);

    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYUP = 0x2;

    /// <summary>
    /// Presses Esc for the app in the foreground, the source of an external drag, whose drag loop takes it as a cancel.
    /// Never for this app, whose own drags are cancelled by their drop source.
    /// </summary>
    public static void CancelForegroundDrag()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        if (processId == Environment.ProcessId)
            return;

        keybd_event(VK_ESCAPE, 0, 0, IntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    public static unsafe void SaveComStreamToFile(IStream source, string filePath)
    {
        // Read, not IStream.CopyTo, which some sources don't implement (Firefox)
        using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        do
        {
            read = 0;
            source.Read(buffer, buffer.Length, (HANDLE)(&read));
            file.Write(buffer, 0, read);
        }
        while (read > 0);
    }
}
