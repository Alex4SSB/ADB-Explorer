using Vanara.PInvoke;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

/// <summary>
/// The write end of a pull that hydrates a placeholder: data is handed to the platform in chunks as it arrives.
/// Chunks are 4 KiB aligned, as the platform requires everywhere but at the end of the file.
/// </summary>
public sealed class CloudTransferStream(CF_OPERATION_INFO opInfo, long fileSize) : Stream
{
    private const int CHUNK_SIZE = 1 << 20;

    private readonly byte[] _buffer = new byte[CHUNK_SIZE];

    private int _buffered;

    private long _transferred;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _transferred + _buffered;

    public override long Position { get => Length; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> data)
    {
        // A file grown on the device since it was listed: the placeholder only holds its listed size
        var room = fileSize - Length;
        if (data.Length > room)
            data = data[..(int)Math.Max(room, 0)];

        while (data.Length > 0)
        {
            var count = Math.Min(data.Length, CHUNK_SIZE - _buffered);
            data[..count].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += count;
            data = data[count..];

            if (_buffered == CHUNK_SIZE)
                Flush();
        }
    }

    public override void Flush()
    {
        if (_buffered == 0)
            return;

        Transfer(_transferred, _buffered);
        _transferred += _buffered;
        _buffered = 0;
    }

    /// <summary>Hands over the rest. A file that shrank on the device since it was listed fails the hydration.</summary>
    public void Complete()
    {
        Flush();

        if (_transferred < fileSize)
            Fail(opInfo, _transferred, fileSize - _transferred);
    }

    public void FailRemaining(long requiredOffset, long requiredLength)
    {
        var start = Math.Min(_transferred, requiredOffset);
        Fail(opInfo, start, Math.Max(fileSize - start, requiredLength));
    }

    public static void Fail(CF_OPERATION_INFO opInfo, long offset, long length)
    {
        var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERDATA
        {
            CompletionStatus = NTStatus.STATUS_UNSUCCESSFUL,
            Offset = offset,
            Length = length,
        });

        CfExecute(opInfo, ref opParams);
    }

    private unsafe void Transfer(long offset, int length)
    {
        fixed (byte* data = _buffer)
        {
            var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERDATA
            {
                CompletionStatus = NTStatus.STATUS_SUCCESS,
                Buffer = (IntPtr)data,
                Offset = offset,
                Length = length,
            });

            CfExecute(opInfo, ref opParams).ThrowIfFailed();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
