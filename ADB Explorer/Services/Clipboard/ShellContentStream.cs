using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static ADB_Explorer.Services.NativeMethods;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace ADB_Explorer.Services;

/// <summary>
/// The content of a virtual file (a zip member, a file on a phone over MTP, a FileContents stream) as a
/// read-only, forward-only stream. Every read fills its buffer unless the content ends, as Push expects.
/// </summary>
public sealed unsafe class ShellContentStream : Stream
{
    private object? _comStream;

    private ShellContentStream(object comStream) => _comStream = comStream;

    /// <summary>
    /// The content of the shell item at <paramref name="parsingName"/>, through its BHID_Stream handler. Opened anew on
    /// this thread, as reading through <paramref name="item"/> would run every call on the UI thread that created it.
    /// </summary>
    public static ShellContentStream Open(ShellItem? item, string parsingName)
    {
        try
        {
            using var opened = ShellItem.Open(parsingName);

            return new(GetStream(opened));
        }
        catch when (item is not null)
        {
            return new(GetStream(item));
        }
    }

    private static IStream GetStream(ShellItem item)
        => item.GetHandler<IStream>(Shell32.ShellUtil.CreateBindCtx(STGM.STGM_READ), Shell32.BHID.BHID_Stream);

    /// <summary>The FileContents stream of the <paramref name="index"/>th file described by <paramref name="dataObject"/>.</summary>
    public static ShellContentStream FromDataObject(ComDataObject dataObject, int index)
        => new(VirtualFileDataObject.GetFileContentsObject(dataObject, index));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_comStream is null, this);

        var total = 0;
        fixed (byte* start = buffer)
        {
            while (total < buffer.Length)
            {
                var read = ReadChunk(start + total, buffer.Length - total);
                if (read == 0)
                    break;

                total += read;
            }
        }

        return total;
    }

    private int ReadChunk(byte* destination, int count)
    {
        int read = 0;

        // Our own streams (another instance of the app) are managed objects, not COM proxies
        if (_comStream is IPointerStream pointerStream)
        {
            var hr = pointerStream.Read((HANDLE)destination, count, (HANDLE)(&read));
            if (hr is not (HResult.Ok or HResult.False))
                throw new IOException($"Reading the shell's stream failed (0x{(int)hr:X8})", (int)hr);

            return read;
        }

        var managed = new byte[count];
        ((IStream)_comStream!).Read(managed, count, (HANDLE)(&read));
        managed.AsSpan(0, read).CopyTo(new Span<byte>(destination, count));

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (_comStream is not null && Marshal.IsComObject(_comStream))
            Marshal.ReleaseComObject(_comStream);

        _comStream = null;
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
