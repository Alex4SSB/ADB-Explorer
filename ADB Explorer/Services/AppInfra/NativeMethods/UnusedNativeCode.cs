using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ADB_Explorer.Services;

public static partial class NativeMethods
{

#if false

    private static class DragLoop
    {
        private static byte[] FALSE => new byte[] { 0, 0, 0, 0 };
        private static byte[] TRUE => new byte[] { 1, 0, 0, 0 };

        private static VirtualFileDataObject dataObject;

        private static int totalCount = 0;

        private static int completedCount = 0;

        private static void AddCompletedOp()
        {
            completedCount++;

            if (completedCount < totalCount)
                return;

            // Set InShellDragLoop value to 0 == loop completed
            dataObject.UpdateData(VirtualFileDataObject.DRAGLOOP, FALSE);

            // Return everything to initial state
            totalCount = 0;
            completedCount = 0;
        }

        public static void SetDragLoop(VirtualFileDataObject vfdo, IEnumerable<FileSyncOperation> fileOperations)
        {
            dataObject = vfdo;

            // Add InShellDragLoop object to the VFDO, with value of 1 == loop in progress
            vfdo.SetData(VirtualFileDataObject.DRAGLOOP, TRUE);
            totalCount = fileOperations.Count();

            foreach (var fileOp in fileOperations)
            {
                // Add the event handler to all operations
                fileOp.PropertyChanged += FileOp_PropertyChanged;
            }

            void FileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(FileSyncOperation.Status) && ((FileSyncOperation)sender).Status
                    is not FileOperation.OperationStatus.InProgress
                    and not FileOperation.OperationStatus.Waiting)
                {
                    // Increase the counter if the operation is done, even if failed
                    AddCompletedOp();

                    // Remove the event handler from the operation since file operations are not disposed of regularly
                    ((FileSyncOperation)sender).PropertyChanged -= FileOp_PropertyChanged;
                }
            }
        }
    }

    //
    //  https://learn.microsoft.com/en-us/windows/win32/shell/clipboard
    //

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(HANDLE hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(HANDLE hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(HANDLE hDC, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(HANDLE hDC, HANDLE h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(HANDLE hDC);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(HANDLE hWnd, HANDLE hDC);

    public static HANDLE HdcFromBitmap(System.Drawing.Bitmap bitmap)
    {
        var screenHdc = GetDC(IntPtr.Zero);
        var compatibeHdc = CreateCompatibleDC(screenHdc);
        var bmpHandle = CreateCompatibleBitmap(screenHdc, bitmap.Width, bitmap.Height);

        using (var g = System.Drawing.Graphics.FromHdc(compatibeHdc))
        {
            g.DrawImage(bitmap, new POINT(0, 0));
        }

        var hBitmap = SelectObject(compatibeHdc, bmpHandle);

        DeleteDC(compatibeHdc);
        ReleaseDC(IntPtr.Zero, screenHdc);

        return hBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHDRAGIMAGE : IByteStruct
    {
        public SIZE sizeDragImage;
        public POINT ptOffset;
        public HANDLE hbmpDragImage;
        public uint crColorKey;
        public byte[] bitmapArray;

        public readonly IEnumerable<byte> Bytes => BytesFromStructure(this).Concat(bitmapArray);

        //public static SHDRAGIMAGE FromBytes(IEnumerable<byte> bytes) => StructureFromBytes<SHDRAGIMAGE>(bytes);

        public SHDRAGIMAGE(System.Drawing.Bitmap bitmap)
        {
            sizeDragImage.Width = bitmap.Width;
            sizeDragImage.Height = bitmap.Height;

            System.Drawing.Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
            var argbBtimap = bitmap.Clone(rect, PixelFormat.Format32bppPArgb);
            argbBtimap.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);

            hbmpDragImage = HdcFromBitmap(argbBtimap);

            var bitmapData = argbBtimap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

            int bytes = bitmapData.Stride * argbBtimap.Height;
            bitmapArray = new byte[bytes];

            var ptr = bitmapData.Scan0;
            Marshal.Copy(ptr, bitmapArray, 0, bytes);

            hbmpDragImage = ptr;

            //bitmap.UnlockBits(bitmapData);
        }

        public static SHDRAGIMAGE FromStream(MemoryStream stream)
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

            image.bitmapArray = tmp.ToArray();

            return image;
        }
    }

    public class DragBitmap
    {
        public System.Drawing.Bitmap Bitmap { get; internal set; }

        public BitmapSource BitmapSource { get; internal set; }

        public Point Offset { get; internal set; }

        public Color Background { get; internal set; }
    }

    public static DragBitmap GetBitmapFromShell(MemoryStream stream)
    {
        var shImage = SHDRAGIMAGE.FromStream(stream);
        var bitmap = shImage.bitmapArray;

        var bitmapSource = BitmapSource.Create(shImage.sizeDragImage.Width,
                                               shImage.sizeDragImage.Height,
                                               shImage.sizeDragImage.Width,
                                               shImage.sizeDragImage.Height,
                                               PixelFormats.Pbgra32,
                                               null,
                                               bitmap,
                                               shImage.sizeDragImage.Width * 4);

        return New(bitmapSource, shImage);


        static DragBitmap New(BitmapSource bitmapSource, SHDRAGIMAGE shImage)
        {
            DragBitmap bitmap = new()
            {
                BitmapSource = bitmapSource,
                Offset = shImage.ptOffset,
                // For some reason, File Explorer returns white even when bacground is transparent
                Background = (shImage.sizeDragImage.Width == 96 && shImage.sizeDragImage.Height == 96) ? Colors.White : Colors.Transparent,
            };

            return bitmap;
        }
    }

    public void SendDragBitmapToShell(DragBitmap bitmap)
    {
        SHDRAGIMAGE image = new(bitmap.Bitmap)
        {
            ptOffset = bitmap.Offset,
            crColorKey = bitmap.Background == Colors.White ? 0xFFFFFFFF : 0x00000000,
        };

        SetData(DRAGIMAGE, image.Bytes);

        //try
        //{
        //    IDragSourceHelper sourceHelper = (IDragSourceHelper)new DragDropHelper();

        //    sourceHelper.InitializeFromBitmap(ref image, this);
        //}
        //catch
        //{
        //    //MDeleteObject(image.hbmpDragImage);
        //}

    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern HANDLE ILCreateFromPath(string path);

    [DllImport("Shell32.dll", CharSet = CharSet.None)]
    private static extern void ILFree(HANDLE pidl);

    [DllImport("Shell32.dll", CharSet = CharSet.None)]
    private static extern int ILGetSize(HANDLE pidl);

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(HANDLE pidl, StringBuilder pszPath);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern HANDLE ILFindLastID(HANDLE pidl);
    
    public static Action<Stream> VirtualShellIDList(IEnumerable<FileClass> files, string targetPath) =>
        (stream) =>
        {
            var targetID = new ItemIDList(targetPath);

            var virtualFiles = files.Select(file => new SHItemID(new VirtualFileID(file))).ToList();

            stream.Write(BitConverter.GetBytes(virtualFiles.Count));

            int[] sizes = virtualFiles.Select(file => (int)file.cb).Prepend(targetID.Bytes.Length).ToArray();
            int pidlOffset = 4 * (virtualFiles.Count + 2);

            foreach (var size in sizes)
            {
                stream.Write(BitConverter.GetBytes(pidlOffset));
                pidlOffset += size;
            }

            stream.Write(targetID.Bytes);
            foreach (var file in virtualFiles)
                stream.Write(file.Bytes.ToArray());
        };

    /*
        cidl : number of PIDLs that are being transferred, not including the parent folder.
        aoffset : An array of offsets, relative to the beginning of this structure.
        aoffset[0] - fully qualified PIDL of a parent folder.
               If this PIDL is empty, the parent folder is the desktop.
        aoffset[1] ... aoffset[cidl] : offset to one of the PIDLs to be transferred.
        All of these PIDLs are relative to the PIDL of the parent folder.
    */
    public static Action<Stream> CreateShellIDList(IEnumerable<string> filenames) =>
        (stream) =>
        {
            // Get PIDL of all files and add root PIDL
            var pidls = filenames.Select(file => new ItemIDList(file));

            // Since the CIDA is of a dynamic length, it is passed as a stream rather than a byte array
            //using var memStream = new MemoryStream();
            //using var sw = new BinaryWriter(memStream);

            // Initialize CIDA with count of files
            stream.Write(BytesFromStructure(filenames.Count()).ToArray());

            // Offset in bytes from start of stream to first real PIDL
            // Size of int is 4 bytes. Before real PIDLs there is: file count, the offsets to the PIDLs, and the root PIDL.
            // Hence the offset to a 1-file PIDL will be 12
            // In a UNC path the root is a lot longer though
            int pidlOffset = 4 * (filenames.Count() + 2);

            foreach (var pidl in pidls)
            {
                // Write all offsets, including root
                stream.Write(BytesFromStructure(pidlOffset).ToArray());
                pidlOffset += pidl.Bytes.Length;
            }

            // Write the PIDLs after the offsets
            foreach (var pidl in pidls)
                stream.Write(pidl.Bytes);
        };

    public static IEnumerable<ItemIDList> ExtractItemIDListFromStream(MemoryStream stream)
    {
        using var memStream = new MemoryStream();
        using var sr = new BinaryReader(stream);

        var count = sr.ReadUInt32();
        uint[] lengths = new uint[count + 1];

        for (int i = 0; i < count + 2; i++)
        {
            if (i < lengths.Length)
            {
                lengths[i] = sr.ReadUInt32();
                if (i > 0)
                    lengths[i - 1] = lengths[i] - lengths[i - 1];
            }
            else
            {
                lengths[i - 1] = (uint)sr.BaseStream.Length - lengths[i - 1];
            }
        }

        foreach (var len in lengths)
        {
            yield return new ItemIDList(sr.ReadBytes((int)len));
        }
    }

    public static IEnumerable<string> ParseShellIDList(MemoryStream stream)
    {
        var pidls = ExtractItemIDListFromStream(stream);

        // TODO: handle special cases

        return pidls.Select(p => p.Path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct VirtualFileID : IByteStruct
    {
        readonly uint reserved1;
        readonly ushort reserved2;
        public ulong size;
        public ulong packedSize;
        public uint isFile8;
        readonly uint reserved3;
        public FileFlagsAndAttributes flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
        public string dateModified;
        readonly uint reserved4;
        readonly uint reserved5;
        public int nameLength;
        public int parentLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string name;
        readonly ushort nameTerminator;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string parentName;
        readonly ushort parentTerminator;
        readonly uint reserved6;

        public readonly IEnumerable<byte> Bytes
        {
            get
            {
                List<byte> bytes = new();

                bytes.AddRange(BitConverter.GetBytes(reserved1));
                bytes.AddRange(BitConverter.GetBytes(reserved2));
                bytes.AddRange(BitConverter.GetBytes(size));
                bytes.AddRange(BitConverter.GetBytes(packedSize));
                bytes.AddRange(BitConverter.GetBytes(isFile8));
                bytes.AddRange(BitConverter.GetBytes(reserved3));
                bytes.AddRange(BitConverter.GetBytes((uint)flags));
                bytes.AddRange(Encoding.Unicode.GetBytes(dateModified));
                bytes.AddRange(BitConverter.GetBytes(reserved4));
                bytes.AddRange(BitConverter.GetBytes(reserved5));
                bytes.AddRange(BitConverter.GetBytes(nameLength));
                bytes.AddRange(BitConverter.GetBytes(parentLength));
                bytes.AddRange(Encoding.Unicode.GetBytes(name));
                bytes.AddRange(BitConverter.GetBytes(nameTerminator));
                bytes.AddRange(Encoding.Unicode.GetBytes(parentName));
                bytes.AddRange(BitConverter.GetBytes(parentTerminator));
                bytes.AddRange(BitConverter.GetBytes(reserved6));

                return bytes;
            }
        }

        public VirtualFileID(MemoryStream stream)
        {
            using BinaryReader br = new(stream);

            reserved1 = br.ReadUInt32();
            reserved2 = br.ReadUInt16();
            size = br.ReadUInt64();
            packedSize = br.ReadUInt64();
            isFile8 = br.ReadUInt32();
            reserved3 = br.ReadUInt32();
            flags = (FileFlagsAndAttributes)br.ReadUInt32();
            dateModified = Encoding.Unicode.GetString(br.ReadBytes(40));
            reserved4 = br.ReadUInt32();
            reserved5 = br.ReadUInt32();
            nameLength = br.ReadInt32();
            parentLength = br.ReadInt32();
            name = Encoding.Unicode.GetString(br.ReadBytes(nameLength * 2));
            nameTerminator = br.ReadUInt16();
            parentName = Encoding.Unicode.GetString(br.ReadBytes(parentLength * 2));
            parentTerminator = br.ReadUInt16();
            reserved6 = br.ReadUInt32();
        }

        public VirtualFileID(FileClass file, string dest = "")
        {
            if (file.Size is not null && !file.IsDirectory)
            {
                packedSize =
                size = file.Size.Value;
            }

            isFile8 = 8;
            flags = FileFlagsAndAttributes.FILE_ATTRIBUTE_VIRTUAL;
            if (file.IsDirectory)
            {
                flags |= FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
                isFile8 = 0;
            }

            dateModified = file.ModifiedTime?.ToString("MM/dd/yyyy  HH:mm:ss");
            name = file.FullName;
            parentName = string.IsNullOrEmpty(dest) ? "" : dest + '/';

            nameLength = name.Length;
            parentLength = parentName.Length;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHItemID : IByteStruct
    {
        public ushort cb;
        public byte[] data;

        public readonly IEnumerable<byte> Bytes => BytesFromStructure(cb).AppendRange(data);

        public SHItemID(ref byte[] bytes)
        {
            var len = bytes[..2];
            cb = BitConverter.ToUInt16(len);

            if (cb > 0)
            {
                var tempBytes = bytes[..cb];
                data = tempBytes[2..];

                bytes = bytes[cb..];
            }
            else
            {
                data = Array.Empty<byte>();

                bytes = bytes[2..];
            }
        }

        public SHItemID(params byte[] bytes)
            : this(ref bytes)
        { }

        public SHItemID(VirtualFileID fileID)
        {
            data = fileID.Bytes.ToArray();
            cb = (ushort)(data.Length + 2);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ItemIDList
    {
        public byte[] Bytes { get; }

        private string path = null;
        public string Path
        {
            get
            {
                if (path is null)
                {
                    // Create pointer and copy the PIDL to it
                    var ptr = Marshal.AllocHGlobal(Bytes.Length);
                    Marshal.Copy(Bytes, 0, ptr, Bytes.Length);

                    // Get the path from the PIDL and free the pointer
                    StringBuilder path = new();
                    SHGetPathFromIDList(ptr, path);
                    Marshal.FreeHGlobal(ptr);

                    return path.ToString();
                }

                return path;
            }
        }

        public IEnumerable<SHItemID> Children
        {
            get
            {
                byte[] bytes = Bytes.ToArray();
                while (bytes.Length > 0)
                {
                    SHItemID item = new(ref bytes);

                    if (item.cb > 0)
                        yield return item;
                }
            }
        }

        public ItemIDList(params byte[] bytes)
        {
            Bytes = bytes;
        }

        public ItemIDList(string path)
        {
            this.path = path;

            // Get pointer to and size of PIDL
            var pidl = ILCreateFromPath(path);
            int pidlSize = ILGetSize(pidl);

            // Copy PIDL to managed memory
            Bytes = new byte[pidlSize];
            Marshal.Copy(pidl, Bytes, 0, pidlSize);

            // Free the PIDL structure
            ILFree(pidl);
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
                List<byte> bytes = new();

                dropFiles.pFiles = (uint)Marshal.SizeOf<DROPFILES>();
                dropFiles.fWide = false;

                bytes.AddRange(BytesFromStructure(dropFiles));

                //bytes.AddRange(Encoding.Unicode.GetBytes(string.Join('\0', fileNames) + "\0\0"));
                bytes.AddRange(Encoding.UTF8.GetBytes(string.Join('\0', fileNames) + "\0\0"));

                return bytes;
            }
        }

        public CFHDROP(IEnumerable<string> fileNames)
        {
            this.fileNames = fileNames.ToArray();
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

#endif

}
