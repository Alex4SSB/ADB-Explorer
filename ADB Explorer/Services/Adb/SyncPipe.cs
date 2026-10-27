namespace ADB_Explorer.Services;

/// <summary>
/// Connects a sync pull to a push or a shell stream without a file in between. One writer, one reader;
/// a full pipe holds the writer back. Its memory exists only from the first write until read or closed.
/// </summary>
/// <param name="canBorrow">Whether the pipe may buffer beyond its own <see cref="Capacity"/>, in <see cref="TransferMemory"/>.</param>
public sealed unsafe class SyncPipe(long? sizeHint = null, bool canBorrow = false)
{
    /// <summary>A pipe this small already keeps a pull and a push at full speed.</summary>
    public const int Capacity = 256 * 1024;

    // Borrowed memory comes in chunks this big, each freed as soon as it's read
    private const int BorrowedChunkSize = 1024 * 1024;
    private const int MinBorrowedChunkSize = 64 * 1024;

    // How often a writer waiting for memory another pipe may have freed tries again
    private const int BorrowRetryMs = 50;

    private readonly long _ownCapacity = Math.Clamp(sizeHint ?? Capacity, 1, Capacity);
    private readonly object _lock = new();
    private readonly Queue<Chunk> _chunks = new();
    private Chunk? _tail;
    private long _ownAllocated;
    private long _count;
    private bool _isCompleted;
    private bool _isReaderClosed;
    private bool _isReleased;
    private Exception? _error;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Its own memory comes in halves, so the writer fills one while the reader drains the other
    private int OwnChunkSize => (int)Math.Max(1, (_ownCapacity + 1) / 2);

    public long BytesRead { get; private set; }

    public long BytesWritten { get; private set; }

    public bool IsReaderClosed => _isReaderClosed;

    /// <summary>The writer ended the pipe with an error.</summary>
    public bool IsFailed => _error is not null;

    /// <summary>Completes once the reader is done: it read to the end (or the error), or it closed the pipe.</summary>
    public Task Drained => _drained.Task;

    public Stream Writer => field ??= new PipeWriter(this);

    /// <summary>Every read fills the buffer it's given unless the pipe ends, which Push and IStream callers expect.</summary>
    public Stream Reader => field ??= new PipeReader(this);

    public void Write(ReadOnlySpan<byte> data)
    {
        while (data.Length > 0)
        {
            lock (_lock)
            {
                while (!_isReaderClosed && _tail?.Room is not > 0 && !TryAddChunk())
                    Monitor.Wait(_lock, BorrowRetryMs);

                if (_isReaderClosed || _isReleased)
                    throw new IOException("The pipe's reader is closed");

                var length = Math.Min(data.Length, _tail!.Room);
                data[..length].CopyTo(new Span<byte>((byte*)_tail.Data + _tail.End, length));
                _tail.End += length;
                _count += length;
                BytesWritten += length;
                data = data[length..];
                Monitor.PulseAll(_lock);
            }
        }
    }

    private bool TryAddChunk()
    {
        if (_isReleased)
            return false;

        Chunk chunk;
        if (_ownAllocated < _ownCapacity)
        {
            // The second half of an odd capacity is a byte smaller
            chunk = new((int)Math.Min(OwnChunkSize, _ownCapacity - _ownAllocated), isBorrowed: false);
            _ownAllocated += chunk.Size;
        }
        else
        {
            // Sized by what's left of the file, so a small tail doesn't hold a whole chunk
            var left = (sizeHint ?? long.MaxValue) - BytesWritten;
            var size = (int)Math.Clamp(left, MinBorrowedChunkSize, BorrowedChunkSize);
            if (!canBorrow || !TransferMemory.TryReserve(size))
                return false;

            chunk = new(size, isBorrowed: true);
        }

        _chunks.Enqueue(chunk);
        _tail = chunk;

        return true;
    }

    /// <summary>Ends the pipe; with an <paramref name="error"/> the reader fails instead of reaching the end.</summary>
    public void Complete(Exception? error = null)
    {
        lock (_lock)
        {
            _isCompleted = true;
            _error ??= error;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>The reader is done with the pipe, so a writer waiting for room fails instead of hanging.</summary>
    public void CloseReader()
    {
        lock (_lock)
        {
            _isReaderClosed = true;
            Release();
            Monitor.PulseAll(_lock);
        }
    }

    public int Read(Span<byte> destination)
    {
        var total = 0;
        lock (_lock)
        {
            while (total < destination.Length)
            {
                while (_count == 0 && !_isCompleted)
                    Monitor.Wait(_lock);

                if (_error is not null)
                {
                    Release();
                    throw new IOException(_error.Message, _error);
                }

                if (_count == 0)
                {
                    Release();
                    break;
                }

                var head = _chunks.Peek();
                var length = Math.Min(destination.Length - total, head.End - head.Start);
                new ReadOnlySpan<byte>((byte*)head.Data + head.Start, length).CopyTo(destination[total..]);
                head.Start += length;
                _count -= length;
                total += length;

                // Only a full chunk is done with; the last one may still be written to
                if (head.Start == head.Size)
                    FreeChunk(_chunks.Dequeue());

                Monitor.PulseAll(_lock);
            }

            BytesRead += total;
        }

        return total;
    }

    private void FreeChunk(Chunk chunk)
    {
        if (chunk == _tail)
            _tail = null;

        if (chunk.IsBorrowed)
            TransferMemory.Release(chunk.Size);
        else
            _ownAllocated -= chunk.Size;

        chunk.Free();
    }

    private void Release()
    {
        _isReleased = true;
        while (_chunks.Count > 0)
            FreeChunk(_chunks.Dequeue());

        _count = 0;
        _drained.TrySetResult();
    }

    private sealed class Chunk(int size, bool isBorrowed)
    {
        public nint Data { get; } = (nint)NativeMemory.Alloc((nuint)size);

        public int Size { get; } = size;

        public bool IsBorrowed { get; } = isBorrowed;

        public int Start;

        public int End;

        public int Room => Size - End;

        public void Free() => NativeMemory.Free((void*)Data);
    }

    private sealed class PipeWriter(SyncPipe pipe) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => pipe.Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => pipe.Write(buffer);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class PipeReader(SyncPipe pipe) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => pipe.Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => pipe.Read(buffer);
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
