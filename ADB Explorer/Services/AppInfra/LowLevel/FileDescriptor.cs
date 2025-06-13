using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using Vanara.Windows.Shell;
using static ADB_Explorer.Services.NativeMethods;

namespace ADB_Explorer.Services;

public class FileGroup
{
    public readonly IEnumerable<FileDescriptor> FileDescriptors;

    private FILEGROUPDESCRIPTOR GroupDescriptor;

    public IEnumerable<byte> GroupDescriptorBytes => GroupDescriptor.Bytes;

    public IEnumerable<Action<Stream>> DataStreams => FileDescriptors.Select(f => f.StreamContents);

    public FileGroup(IEnumerable<FileDescriptor> fileDescriptors)
    {
        FileDescriptors = fileDescriptors;

        GroupDescriptor = new(FileDescriptors);
    }
}

/// <summary>
/// Class representing a virtual file for use by drag/drop or the clipboard.
/// </summary>
public class FileDescriptor
{
    public string Name { get; set; }

    public Int64? Length { get; set; }

    public DateTime? ChangeTimeUtc { get; set; }

    /// <summary>
    /// Gets or sets an Action that returns the contents of the file.
    /// </summary>
    public Action<Stream> StreamContents { get; set; }

    public Func<bool> SaveToFile { get; set; }

    public bool IsDirectory { get; set; }

    public string SourcePath { get; set; }

    public override string ToString() => Name;

    public FileDescriptor()
    {
    }

    public FileDescriptor(ShellItem shellItem)
    {
        // Remote file
        if (shellItem.ParsingName.Contains("//uuid:"))
        {
            SourcePath = shellItem.ParsingName;

            try
            {
                shellItem.Properties.TryGetValue<string>(Vanara.PInvoke.Ole32.PROPERTYKEY.System.FileName, out var name);
                Name = name;
            }
            catch
            {
                Name = shellItem.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing);
            }
        }
        else
            Name = shellItem.ParsingName;

        Length = shellItem.IsFolder ? null : shellItem.PIDL.Size;
        IsDirectory = shellItem.IsFolder;
    }

    public static FileDescriptor[] GetDescriptors(IDataObject dataObject)
    {
        if (Data.CopyPaste.IsSelf)
            return VirtualFileDataObject.SelfFileGroup?.FileDescriptors?.ToArray();

        try
        {
            if (dataObject.GetData(AdbDataFormats.FileDescriptor) is not MemoryStream fdStream)
                return null;

            var fileGroup = FILEGROUPDESCRIPTOR.FromStream(fdStream);
            return fileGroup.descriptors.Select(FILEDESCRIPTOR.GetFile)?.ToArray();
        }
        catch (COMException)
        {
            // Usually HResult.DV_E_FORMATETC
            // This happens when GetData in our own VFDO isn't ready yet
        }

        return null;
    }

    public static FileDescriptor[] GetFiles(IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(AdbDataFormats.FileContents))
            return null;

        var descriptors = GetDescriptors(dataObject);
        for (int i = 0; i < descriptors.Length; i++)
        {
            var index = i;
            var descriptor = descriptors[i];
            descriptor.SaveToFile = () =>
            {
                // Directories do not have a content stream (but the index in the FileContent stream array is still reserved)
                if (descriptor.IsDirectory)
                    return false;

                FileContentsStream stream;
                try
                {
                    stream = VirtualFileDataObject.GetFileContents(dataObject, index);
                }
                catch (COMException e)
                {
                    // This happens on Windows 11 with names longer than 120 characters (as of 23H2), but not on Windows 10
                    if (e.HResult == (int)HResult.PATH_TOO_LONG)
                    {
                        // TODO: Notify the user of too nested files
                    }

                    return false;
                }

                var fullPath = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, descriptor.Name, '\\');
                Directory.CreateDirectory(FileHelper.GetParentPath(fullPath));
                stream.Save(fullPath);
                return true;
            };
        }

        // TODO: build the file tree

        return descriptors;
    }
}

[StructLayout(LayoutKind.Sequential)]
struct FILEGROUPDESCRIPTOR : IByteStruct
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

    public FILEGROUPDESCRIPTOR(IEnumerable<FileDescriptor> fileDescriptors)
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
struct FILEDESCRIPTOR : IByteStruct
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

    public FILEDESCRIPTOR(FileDescriptor file)
    {
        cFileName = file.Name;

        dwFlags |= FD_FLAGS.FD_PROGRESSUI | FD_FLAGS.FD_ATTRIBUTES | FD_FLAGS.FD_UNICODE;
        dwFileAttributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_VIRTUAL;

        // Set optional directory flag
        if (file.IsDirectory)
        {
            dwFileAttributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
        }

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

    public static FileDescriptor GetFile(FILEDESCRIPTOR descriptor) => new()
    {
        Name = descriptor.cFileName,
        IsDirectory = descriptor.dwFlags.HasFlag(FD_FLAGS.FD_ATTRIBUTES) && descriptor.dwFileAttributes.HasFlag(FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY),
        Length = descriptor.dwFlags.HasFlag(FD_FLAGS.FD_FILESIZE) ? descriptor.nFileSize.GetSize() : null,
        ChangeTimeUtc = descriptor.ftLastWriteTime.DateTimeUTC,
    };

    public static FILEDESCRIPTOR FromBytes(IEnumerable<byte> bytes) => StructureFromBytes<FILEDESCRIPTOR>(bytes);
}
