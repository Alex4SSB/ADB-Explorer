using System.Runtime.InteropServices.ComTypes;
using static ADB_Explorer.Services.NativeMethods;
using STGTY = Vanara.PInvoke.Ole32.STGTY;

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
    public IntPtr ToComPointer() => Marshal.GetComInterfaceForObject(this, typeof(IPointerStream));

    // Runs once the shell released its last reference: a pull it stopped reading would otherwise wait forever on a full pipe
    ~DevicePullStream() => pipe.CloseReader();

    public HRESULT Read(IntPtr pv, int cb, IntPtr pcbRead)
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
            return HRESULT.E_FAIL;
        }

        _position += read;
        if (pcbRead != 0)
            *(int*)pcbRead = read;

        return read < cb ? HRESULT.S_FALSE : HRESULT.S_OK;
    }

    public HRESULT Seek(long dlibMove, SeekOrigin dwOrigin, IntPtr plibNewPosition)
    {
        var target = dwOrigin switch
        {
            SeekOrigin.Begin => dlibMove,
            SeekOrigin.Current => _position + dlibMove,
            _ => (size ?? _position) + dlibMove,
        };

        if (target != _position)
            return HRESULT.STG_E_INVALIDFUNCTION;

        if (plibNewPosition != 0)
            *(long*)plibNewPosition = _position;

        return HRESULT.S_OK;
    }

    public HRESULT Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new() { type = (int)STGTY.STGTY_STREAM, cbSize = size ?? 0 };

        return HRESULT.S_OK;
    }

    public HRESULT Write(IntPtr pv, int cb, IntPtr pcbWritten) => HRESULT.E_NOTIMPL;

    public HRESULT SetSize(long libNewSize) => HRESULT.E_NOTIMPL;

    public HRESULT CopyTo(IntPtr pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => HRESULT.E_NOTIMPL;

    public HRESULT Commit(int grfCommitFlags) => HRESULT.S_OK;

    public HRESULT Revert() => HRESULT.E_NOTIMPL;

    public HRESULT LockRegion(long libOffset, long cb, int dwLockType) => HRESULT.E_NOTIMPL;

    public HRESULT UnlockRegion(long libOffset, long cb, int dwLockType) => HRESULT.E_NOTIMPL;

    public HRESULT Clone(out IntPtr ppstm)
    {
        ppstm = 0;

        return HRESULT.E_NOTIMPL;
    }
}
