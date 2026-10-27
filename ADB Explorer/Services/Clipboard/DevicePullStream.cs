using System.Runtime.InteropServices.ComTypes;
using static ADB_Explorer.Services.NativeMethods;

namespace ADB_Explorer.Services;

/// <summary>
/// The FileContents stream the shell reads for a device file: forward-only, over the <see cref="SyncPipe"/>
/// a pull fills. The shell only seeks to where it already is, which is all this supports.
/// </summary>
/// <param name="firstRead">Called when the shell first reads, not just opens, the stream.</param>
public sealed unsafe class DevicePullStream(SyncPipe pipe, long? size, Action? firstRead = null) : IPointerStream
{
    private long _position;
    private Action? _firstRead = firstRead;

    /// <summary>A COM pointer for the shell's STGMEDIUM, which takes over its reference.</summary>
    public HANDLE ToComPointer() => Marshal.GetComInterfaceForObject(this, typeof(IPointerStream));

    // Runs once the shell released its last reference: a pull it stopped reading would otherwise wait forever on a full pipe
    ~DevicePullStream() => pipe.CloseReader();

    public HResult Read(HANDLE pv, int cb, HANDLE pcbRead)
    {
        Interlocked.Exchange(ref _firstRead, null)?.Invoke();

        int read;
        try
        {
            read = pipe.Read(new Span<byte>((void*)pv, cb));
        }
        catch (IOException e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"DevicePullStream read failed: {e.Message}");
#endif
            return HResult.Fail;
        }

        _position += read;
        if (pcbRead != 0)
            *(int*)pcbRead = read;

        return read < cb ? HResult.False : HResult.Ok;
    }

    public HResult Seek(long dlibMove, SeekOrigin dwOrigin, HANDLE plibNewPosition)
    {
        var target = dwOrigin switch
        {
            SeekOrigin.Begin => dlibMove,
            SeekOrigin.Current => _position + dlibMove,
            _ => (size ?? _position) + dlibMove,
        };

        if (target != _position)
            return HResult.StgInvalidFunction;

        if (plibNewPosition != 0)
            *(long*)plibNewPosition = _position;

        return HResult.Ok;
    }

    public HResult Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new() { type = (int)STGTY.STGTY_STREAM, cbSize = size ?? 0 };

        return HResult.Ok;
    }

    public HResult Write(HANDLE pv, int cb, HANDLE pcbWritten) => HResult.NotImplemented;

    public HResult SetSize(long libNewSize) => HResult.NotImplemented;

    public HResult CopyTo(HANDLE pstm, long cb, HANDLE pcbRead, HANDLE pcbWritten) => HResult.NotImplemented;

    public HResult Commit(int grfCommitFlags) => HResult.Ok;

    public HResult Revert() => HResult.NotImplemented;

    public HResult LockRegion(long libOffset, long cb, int dwLockType) => HResult.NotImplemented;

    public HResult UnlockRegion(long libOffset, long cb, int dwLockType) => HResult.NotImplemented;

    public HResult Clone(out HANDLE ppstm)
    {
        ppstm = 0;

        return HResult.NotImplemented;
    }

    private enum STGTY
    {
        STGTY_STREAM = 2,
    }
}
