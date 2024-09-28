namespace ADB_Explorer.Services;

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Runtime.InteropServices.ComTypes;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int fEscapePressed, uint grfKeyState);
        [PreserveSig]
        int GiveFeedback(uint dwEffect);
    }

    [DllImport("Shell32.dll")]
    private static extern int SHCreateStdEnumFmtEtc(uint cfmt, FORMATETC[] afmt, out IEnumFORMATETC ppenumFormatEtc);

    public static int SHCreateStdEnumFmtEtc(int cfmt, IEnumerable<FORMATETC> afmt, out IEnumFORMATETC ppenumFormatEtc)
        => SHCreateStdEnumFmtEtc((uint)cfmt, afmt.ToArray(), out ppenumFormatEtc);

    [return: MarshalAs(UnmanagedType.Interface)]
    [DllImport("Ole32.dll", PreserveSig = false)]
    private static extern IStream CreateStreamOnHGlobal(HANDLE hGlobal, [MarshalAs(UnmanagedType.Bool)] bool fDeleteOnRelease);

    public static IStream MCreateStreamOnHGlobal(HANDLE hGlobal, bool fDeleteOnRelease)
        => CreateStreamOnHGlobal(hGlobal, fDeleteOnRelease);

    [DllImport("Ole32.dll", CharSet = CharSet.Auto, ExactSpelling = true, PreserveSig = false)]
    private static extern void DoDragDrop(IDataObject dataObject, IDropSource dropSource, int allowedEffects, int[] finalEffect);

    public static void MDoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects, int[] finalEffect)
    => DoDragDrop(dataObject, dropSource, (int)allowedEffects, finalEffect);

    [DllImport("Kernel32.dll")]
    private static extern HANDLE GlobalLock(HANDLE hMem);

    public static HANDLE MGlobalLock(HANDLE hMem)
    => GlobalLock(hMem);

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("Kernel32.dll")]
    private static extern bool GlobalUnlock(HANDLE hMem);

    public static void MGlobalUnlock(HANDLE hMem)
    => GlobalUnlock(hMem);

    [DllImport("Kernel32.dll")]
    private static extern HANDLE GlobalSize(HANDLE handle);

    public static HANDLE MGlobalSize(HANDLE handle)
        => GlobalSize(handle);

    public interface IByteStruct
    {
        public IEnumerable<byte> Bytes { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILEGROUPDESCRIPTOR : IByteStruct
    {
        public UInt32 cItems;
        public FILEDESCRIPTOR[] descriptors;

        public readonly IEnumerable<byte> Bytes
        {
            get
            {
                List<byte> bytes = [.. BytesFromStructure(cItems)];

                foreach (var item in descriptors)
                {
                    bytes.AddRange(item.Bytes);
                }

                return bytes;
            }
        }

        public FILEGROUPDESCRIPTOR(IEnumerable<VirtualFileDataObject.FileDescriptor> fileDescriptors)
        {
            descriptors = fileDescriptors.Select(f => new FILEDESCRIPTOR(f)).ToArray();

            cItems = (uint)descriptors.Length;
        }

        public static FILEGROUPDESCRIPTOR FromBytes(IEnumerable<byte> bytes)
            => new()
            {
                cItems = StructureFromBytes<UInt32>(bytes.Take(sizeof(UInt32))),
                descriptors = bytes.Skip(sizeof(UInt32))
                                   .Chunk(Marshal.SizeOf<FILEDESCRIPTOR>())
                                   .Select(FILEDESCRIPTOR.FromBytes)
                                   .ToArray()
            };

        public static FILEGROUPDESCRIPTOR FromStream(MemoryStream stream)
        {
            FILEGROUPDESCRIPTOR fgd = new();
            var fdSize = Marshal.SizeOf<FILEDESCRIPTOR>();
            using BinaryReader reader = new(stream);

            try
            {
                fgd.cItems = reader.ReadUInt32();
            }
            catch (Exception)
            {
                return fgd;
            }

            Array.Resize(ref fgd.descriptors, (int)fgd.cItems);

            for (int i = 0; i < fgd.cItems; i++)
            {
                try
                {
                    var fdBytes = reader.ReadBytes(fdSize);
                    fgd.descriptors[i] = FILEDESCRIPTOR.FromBytes(fdBytes);
                }
                catch
                {
                    break;
                }
            }

            return fgd;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FILEDESCRIPTOR : IByteStruct
    {
        public FD_FLAGS dwFlags;
        public Guid clsid;
        public SIZE sizel;
        public POINT pointl;
        public FileFlagsAndAttributes dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public FILESIZE nFileSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string cFileName;

        public readonly IEnumerable<byte> Bytes => BytesFromStructure(this);

        public FILEDESCRIPTOR(VirtualFileDataObject.FileDescriptor file)
        {
            cFileName = file.Name;

            dwFlags |= FD_FLAGS.FD_ATTRIBUTES | FD_FLAGS.FD_PROGRESSUI;
            dwFileAttributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_VIRTUAL;

            // Set optional directory flag
            if (file.IsDirectory)
                dwFileAttributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;

            // Set optional timestamp
            if (file.ChangeTimeUtc.HasValue)
            {
                dwFlags |= FD_FLAGS.FD_WRITESTIME;

                ftLastWriteTime = new(file.ChangeTimeUtc.Value);
            }

            // Set optional length
            if (file.Length.HasValue)
            {
                dwFlags |= FD_FLAGS.FD_FILESIZE;

                nFileSize = new(file.Length.Value);
            }
        }

        public static FILEDESCRIPTOR FromBytes(IEnumerable<byte> bytes) => StructureFromBytes<FILEDESCRIPTOR>(bytes);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ADBDRAGLIST(ADBService.AdbDevice device, IEnumerable<FileClass> files) : IByteStruct
    {
        public string deviceId = device.ID;
        public string parentFolder = files.First().ParentPath;
        public string[] items = files.Select(f => f.FullName).ToArray();

        public readonly IEnumerable<byte> Bytes
        {
            get
            {
                var joinedItems = string.Join("\0", items);
                var combined = string.Join("\0", deviceId, parentFolder, joinedItems, '\0');
                var bytes = Encoding.Unicode.GetBytes(combined);

                return bytes;
            }
        }

        public static ADBDRAGLIST FromStream(MemoryStream stream)
        {
            ADBDRAGLIST dragList = new();
            var bytes = stream.ToArray();

            int i = 0;
            List<string> strings = [];

            while (i < bytes.Length)
            {
                var index = ByteHelper.PatternAt(bytes, [0, 0], i);

                if (index < 0)
                    break;

                index++;

                string item = Encoding.Unicode.GetString(bytes[i..index]);
                if (string.IsNullOrEmpty(item) || bytes[i..index].Sum(b => (decimal)b) == 0)
                    break;

                strings.Add(item);

                i = index + 2;
            }

            dragList.deviceId = strings[0];
            dragList.parentFolder = strings[1];
            dragList.items = strings.Skip(2).ToArray();

            return dragList;
        }
    }

    /// <summary>
    /// Returns true if the HRESULT is a success code.
    /// </summary>
    public static bool SUCCEEDED(int hr)
    {
        return 0 <= hr;
    }
}
