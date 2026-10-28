using Microsoft.Win32.SafeHandles;

namespace ADB_Explorer.Services;

/// <summary>
/// Connects a sync pull to a push or a shell stream without a file in between. One writer, one reader;
/// a full pipe holds the writer back. Its memory exists only from the first write until read or closed.
/// </summary>
/// <param name="canSpill">Whether, past its own <see cref="Capacity"/>, the pipe buffers in a temp file instead of holding the writer back.</param>
public sealed unsafe class SyncPipe(long? sizeHint = null, bool canSpill = false)
{
    /// <summary>A pipe this small already keeps a pull and a push at full speed.</summary>
    public const int Capacity = 256 * 1024;

    // How often a writer the temp drive had no room for tries to spill again
    private const int SpillRetryMs = 50;

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

    // Once spilling, everything written goes to the file until the reader has caught up with it, which keeps the order:
    // the reader gets to the file only after the memory written before it
    private SafeFileHandle? _spill;
    private bool _isSpilling;
    private long _spillStart;
    private long _spillEnd;

    // Free space a spill leaves on the temp drive: at least this much, or a tenth of the drive
    private const long MinSpillFreeBytes = 5L * 1024 * 1024 * 1024;

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
                while (!_isReaderClosed && !_isSpilling && _tail?.Room is not > 0 && !TryAddChunk() && !TryStartSpill())
                    Monitor.Wait(_lock, SpillRetryMs);

                if (_isReaderClosed || _isReleased)
                    throw new IOException("The pipe's reader is closed");

                int length;
                if (_isSpilling)
                {
                    length = data.Length;
                    RandomAccess.Write(_spill!, data, _spillEnd);
                    _spillEnd += length;
                }
                else
                {
                    length = Math.Min(data.Length, _tail!.Room);
                    data[..length].CopyTo(new Span<byte>((byte*)_tail.Data + _tail.End, length));
                    _tail.End += length;
                }

                _count += length;
                BytesWritten += length;
                data = data[length..];
                Monitor.PulseAll(_lock);
            }
        }
    }

    private bool TryAddChunk()
    {
        if (_isReleased || _ownAllocated >= _ownCapacity)
            return false;

        // The second half of an odd capacity is a byte smaller
        var chunk = new Chunk((int)Math.Min(OwnChunkSize, _ownCapacity - _ownAllocated));
        _ownAllocated += chunk.Size;

        _chunks.Enqueue(chunk);
        _tail = chunk;

        return true;
    }

    /// <summary>Starts writing to the spill file, when the pipe may and the temp drive has room for the rest of the file.</summary>
    private bool TryStartSpill()
    {
        if (!canSpill || _isReleased || !HasSpillRoom(Math.Max(0, (sizeHint ?? 0) - BytesWritten)))
            return false;

        try
        {
            _spill ??= File.OpenHandle(Path.Combine(Path.GetTempPath(), $"ADB Explorer spill {Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.DeleteOnClose);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        _isSpilling = true;

        return true;
    }

    /// <summary>Whether <paramref name="bytes"/> more can be spilled to the temp drive, leaving it enough free space.</summary>
    public static bool HasSpillRoom(long bytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!);

            return drive.AvailableFreeSpace - bytes >= Math.Max(MinSpillFreeBytes, drive.TotalSize / 10);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
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

                if (_chunks.TryPeek(out var head) && head.End > head.Start)
                {
                    var length = Math.Min(destination.Length - total, head.End - head.Start);
                    new ReadOnlySpan<byte>((byte*)head.Data + head.Start, length).CopyTo(destination[total..]);
                    head.Start += length;
                    _count -= length;
                    total += length;

                    // Only a full chunk is done with; the last one may still be written to
                    if (head.Start == head.Size)
                        FreeChunk(_chunks.Dequeue());
                }
                else
                {
                    ReadSpill(destination[total..], ref total);
                }

                Monitor.PulseAll(_lock);
            }

            BytesRead += total;
        }

        return total;
    }

    /// <summary>Reads what was spilled, once the memory before it is read; caught up, the file is emptied and memory used again.</summary>
    private void ReadSpill(Span<byte> destination, ref int total)
    {
        var length = (int)Math.Min(destination.Length, _spillEnd - _spillStart);
        var read = RandomAccess.Read(_spill!, destination[..length], _spillStart);
        if (read == 0)
            throw new IOException("The pipe's spill file ended early");

        _spillStart += read;
        _count -= read;
        total += read;

        if (_spillStart < _spillEnd)
            return;

        _isSpilling = false;
        _spillStart = _spillEnd = 0;
        RandomAccess.SetLength(_spill!, 0);
    }

    private void FreeChunk(Chunk chunk)
    {
        if (chunk == _tail)
            _tail = null;

        _ownAllocated -= chunk.Size;
        chunk.Free();
    }

    private void Release()
    {
        _isReleased = true;
        while (_chunks.Count > 0)
            FreeChunk(_chunks.Dequeue());

        _spill?.Dispose();
        _spill = null;
        _isSpilling = false;
        _spillStart = _spillEnd = 0;
        _count = 0;
        _drained.TrySetResult();
    }

    private sealed class Chunk(int size)
    {
        public nint Data { get; } = (nint)NativeMemory.Alloc((nuint)size);

        public int Size { get; } = size;

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
