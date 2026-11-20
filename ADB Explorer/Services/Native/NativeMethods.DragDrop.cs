namespace ADB_Explorer.Services;

using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.Marshalling;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Ole32;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

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

    // PreserveSig = false throws on failure; a return type would become an extra [out, retval] argument
    [DllImport("Ole32.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void DoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects, out DragDropEffects finalEffect);

    public static DragDropEffects MDoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects)
    {
        DoDragDrop(dataObject, dropSource, allowedEffects, out var finalEffect);

        return finalEffect;
    }

    /// <summary>
    /// CFSTR_DROPDESCRIPTION, written to the source data object by a shell drop target.
    /// </summary>
    public record DropDescription(DROPIMAGETYPE Type, string Message, string Insert)
    {
        /// <summary>True when the target describes the drop, rather than clearing its description.</summary>
        public bool IsValid => Type is not DROPIMAGETYPE.DROPIMAGE_INVALID && !string.IsNullOrEmpty(Message);

        public static DropDescription? FromBytes(byte[] bytes)
        {
            // DWORD type, then two WCHAR[MAX_PATH] strings
            if (bytes.Length < sizeof(int) + MAX_PATH * 2 * 2)
                return null;

            static string ReadString(byte[] bytes, int offset)
            {
                var text = Encoding.Unicode.GetString(bytes, offset, MAX_PATH * 2);
                var end = text.IndexOf('\0');
                return end < 0 ? text : text[..end];
            }

            return new((DROPIMAGETYPE)BitConverter.ToInt32(bytes, 0),
                       ReadString(bytes, sizeof(int)),
                       ReadString(bytes, sizeof(int) + MAX_PATH * 2));
        }
    }

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

    /// <summary>
    /// IStream with its buffers as pointers, so a stream we serve doesn't get a managed array
    /// allocated and copied for every Read the shell makes.
    /// </summary>
    [ComImport]
    [Guid("0000000c-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPointerStream
    {
        [PreserveSig] HRESULT Read(IntPtr pv, int cb, IntPtr pcbRead);
        [PreserveSig] HRESULT Write(IntPtr pv, int cb, IntPtr pcbWritten);
        [PreserveSig] HRESULT Seek(long dlibMove, SeekOrigin dwOrigin, IntPtr plibNewPosition);
        [PreserveSig] HRESULT SetSize(long libNewSize);
        [PreserveSig] HRESULT CopyTo(IntPtr pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten);
        [PreserveSig] HRESULT Commit(int grfCommitFlags);
        [PreserveSig] HRESULT Revert();
        [PreserveSig] HRESULT LockRegion(long libOffset, long cb, int dwLockType);
        [PreserveSig] HRESULT UnlockRegion(long libOffset, long cb, int dwLockType);
        [PreserveSig] HRESULT Stat(out STATSTG pstatstg, int grfStatFlag);
        [PreserveSig] HRESULT Clone(out IntPtr ppstm);
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(HANDLE hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

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
            ReleaseStgMedium(medium);
        }

        CreateStreamOnHGlobal(IntPtr.Zero, true, out var stream).ThrowIfFailed();
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
            ReleaseStgMedium(medium);
        }
    }

    /// <summary>
    /// Hands a COM object to another thread: <paramref name="dataObject"/> is marshaled here, and the returned
    /// function unmarshals it on the thread that calls it (once), so its calls don't go through this one.
    /// </summary>
    public static Func<IDataObject> MarshalToThread(IDataObject dataObject)
    {
        var iid = typeof(IDataObject).GUID;
        CoMarshalInterThreadInterfaceInStream(iid, dataObject, out var stream).ThrowIfFailed();

        return () =>
        {
            CoGetInterfaceAndReleaseStream(stream, iid, out var unmarshaled).ThrowIfFailed();

            return (IDataObject)unmarshaled;
        };
    }

    /// <summary>
    /// Presses Esc for the app in the foreground, the source of an external drag, whose drag loop takes it as a cancel.
    /// Never for this app, whose own drags are cancelled by their drop source.
    /// </summary>
    public static void CancelForegroundDrag()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        if (processId == Environment.ProcessId)
            return;

        keybd_event(VK.VK_ESCAPE, 0, 0);
        keybd_event(VK.VK_ESCAPE, 0, KEYEVENTF.KEYEVENTF_KEYUP);
    }

    public static unsafe void SaveComStreamToFile(IStream source, string filePath)
    {
        // Read, not IStream.CopyTo, which some sources don't implement (Firefox)
        using var file = new FileStream(filePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        do
        {
            read = 0;
            source.Read(buffer, buffer.Length, (IntPtr)(&read));
            file.Write(buffer, 0, read);
        }
        while (read > 0);
    }
}
