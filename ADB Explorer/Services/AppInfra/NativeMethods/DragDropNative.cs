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
    private static extern HResult SHCreateStdEnumFmtEtc(uint cfmt, FORMATETC[] afmt, out IEnumFORMATETC ppenumFormatEtc);

    public static HResult SHCreateStdEnumFmtEtc(int cfmt, IEnumerable<FORMATETC> afmt, out IEnumFORMATETC ppenumFormatEtc)
        => SHCreateStdEnumFmtEtc((uint)cfmt, afmt.ToArray(), out ppenumFormatEtc);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ADBDRAGLIST : IByteStruct
    {
        public string deviceId;
        public string parentFolder;
        public string[] items;

        public ADBDRAGLIST(ADBService.AdbDevice device, IEnumerable<FileClass> files)
        {
            deviceId = device.ID;
            parentFolder = files.First().ParentPath;
            
            items = (parentFolder == AdbExplorerConst.RECYCLE_PATH
                ? files.Select(f => FileHelper.GetFullName(f.FullPath))
                : files.Select(f => f.FullName)).ToArray();
        }

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
                // Index of Unicode chars must be even
                var index = ByteHelper.PatternAt(bytes, [0, 0], i, true);

                if (index < 0)
                    break;

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
}
