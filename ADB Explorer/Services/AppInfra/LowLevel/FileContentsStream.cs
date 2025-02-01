// Copied and modified from https://github.com/asklar/clipview under MIT License

using System.Runtime.InteropServices.ComTypes;

namespace ADB_Explorer.Services.AppInfra;

public class FileContentsStream : IDisposable
{
    private readonly STATSTG stat;

    private readonly IStream stream;

    public FileContentsStream(IStream stream)
    {
        this.stream = stream;
        stream.Stat(out stat, 0);
    }

    public string FileName => stat.pwcsName;
    public long Length => stat.cbSize;

    enum STREAM_SEEK
    {
        STREAM_SEEK_SET,
        STREAM_SEEK_CUR,
        STREAM_SEEK_END
    };

    public void SaveToStream(Stream outputStream)
    {
        // Initialize the buffer to the size of the stream.
        // This should work as long as we are able to create a buffer of such size.
        byte[] buffer = new byte[stat.cbSize];
        stream.Seek(0, (int)STREAM_SEEK.STREAM_SEEK_SET, HANDLE.Zero);

        HANDLE pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            stream.Read(buffer, buffer.Length, pcbRead);
            outputStream.Write(buffer, 0, Marshal.ReadInt32(pcbRead));
        }
        catch (EndOfStreamException)
        { }
        finally
        {
            Marshal.FreeHGlobal(pcbRead);
        }
    }

    public void Save(string filepath, bool disposeAfter = true)
    {
        using var file = File.Create(filepath);
        SaveToStream(file);

        if (disposeAfter)
            Dispose();
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(stream);
    }
}
