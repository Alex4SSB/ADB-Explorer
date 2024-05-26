namespace ADB_Explorer.Services;

using System.Runtime.InteropServices.ComTypes;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

using HANDLE = IntPtr;

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

    public static void DoDragDrop(IDataObject dataObject, IDropSource dropSource, DragDropEffects allowedEffects, int[] finalEffect)
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
                List<byte> bytes = new();
                bytes.AddRange(BytesFromStructure(cItems));

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
                                   .Chunk(Marshal.SizeOf(typeof(FILEDESCRIPTOR)))
                                   .Select(FILEDESCRIPTOR.FromBytes)
                                   .ToArray()
            };

        public static FILEGROUPDESCRIPTOR FromStream(MemoryStream stream)
        {
            FILEGROUPDESCRIPTOR fgd = new();
            var fdSize = Marshal.SizeOf(typeof(FILEDESCRIPTOR));
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
        public Int32 sizelcx;
        public Int32 sizelcy;
        public Int32 pointlx;
        public Int32 pointly;
        public FileFlagsAndAttributes dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public UInt32 nFileSizeHigh;
        public UInt32 nFileSizeLow;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string cFileName;

        public readonly IEnumerable<byte> Bytes => BytesFromStructure(this);

        public FILEDESCRIPTOR(VirtualFileDataObject.FileDescriptor file)
        {
            cFileName = file.Name;

            // Set optional directory flag
            if (file.IsDirectory)
            {
                dwFlags |= FD_FLAGS.FD_ATTRIBUTES;

                dwFileAttributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
            }

            // Set optional timestamp
            if (file.ChangeTimeUtc.HasValue)
            {
                dwFlags |= FD_FLAGS.FD_CREATETIME | FD_FLAGS.FD_WRITESTIME;

                var changeTime = file.ChangeTimeUtc.Value.ToLocalTime().ToFileTime();
                var changeTimeFileTime = new FILETIME
                {
                    dwLowDateTime = (int)(changeTime & 0xffffffff),
                    dwHighDateTime = (int)(changeTime >> 32),
                };

                ftLastWriteTime = changeTimeFileTime;
                ftCreationTime = changeTimeFileTime;
            }

            // Set optional length
            if (file.Length.HasValue)
            {
                dwFlags |= FD_FLAGS.FD_FILESIZE;

                nFileSizeLow = (uint)(file.Length & 0xffffffff);
                nFileSizeHigh = (uint)(file.Length >> 32);
            }
        }

        public static FILEDESCRIPTOR FromBytes(IEnumerable<byte> bytes) => StructureFromBytes<FILEDESCRIPTOR>(bytes);
    }



    //
    //  https://learn.microsoft.com/en-us/windows/win32/shell/clipboard
    //

    [StructLayout(LayoutKind.Sequential)]
    public struct SHDRAGIMAGE : IByteStruct
    {
        public SIZE sizeDragImage;
        public POINT ptOffset;
        public HANDLE hbmpDragImage;
        public uint crColorKey;

        public readonly IEnumerable<byte> Bytes => BytesFromStructure(this);

        public static SHDRAGIMAGE FromBytes(IEnumerable<byte> bytes) => StructureFromBytes<SHDRAGIMAGE>(bytes);

        public static SHDRAGIMAGE FromStream(MemoryStream stream, out byte[] bitmap)
        {
            SHDRAGIMAGE image = new();
            using BinaryReader reader = new(stream);
            List<byte[]> bytes = new();

            try
            {
                image.sizeDragImage.Width = reader.ReadInt32();
                image.sizeDragImage.Height = reader.ReadInt32();
                image.ptOffset.X = reader.ReadInt32();
                image.ptOffset.Y = reader.ReadInt32();
                image.hbmpDragImage = new(reader.ReadInt32());
                image.crColorKey = reader.ReadUInt32();

                // Row length in bytes
                var stride = image.sizeDragImage.Width * 4;

                while (reader.ReadBytes(stride) is byte[] arr && arr.Length == stride)
                {
                    // Read a whole row
                    bytes.Add(arr);
                }
            }
            catch
            { }

            List<byte> tmp = new();

            // Reverse row order to flip image vertically
            bytes.Reverse();
            bytes.ForEach(tmp.AddRange);

            // Not actually part of the struct so returned as an out param
            bitmap = tmp.ToArray();

            return image;
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
