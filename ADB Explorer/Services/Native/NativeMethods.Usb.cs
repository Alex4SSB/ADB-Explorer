using Microsoft.Win32.SafeHandles;

namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    private const int CR_SUCCESS = 0;
    private const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x100;
    private const uint CM_DRP_ADDRESS = 0x1D;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C;

    private static readonly Guid GUID_DEVINTERFACE_USB_HUB = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");

    /// <summary>
    /// The USB version the device with this serial actually runs at (not what it supports), e.g. <c>2.0</c>.
    /// Read from its hub port; <see langword="null"/> when it isn't found.
    /// </summary>
    public static string? GetUsbLinkVersion(string serial)
    {
        try
        {
            if (FindUsbDevice(serial) is not { } devInst)
                return null;

            uint port = 0, size = sizeof(uint);
            if (CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_ADDRESS, out _, ref port, ref size, 0) != CR_SUCCESS
                || CM_Get_Parent(out var hubInst, devInst, 0) != CR_SUCCESS
                || GetDeviceId(hubInst) is not { } hubId
                || GetHubPath(hubId) is not { } hubPath)
                return null;

            using var hub = CreateFileW(hubPath, GENERIC_WRITE, FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
            if (hub.IsInvalid)
                return null;

            // USB_NODE_CONNECTION_INFORMATION_EX_V2: index, length, protocols the caller knows (1.1|2.0|3.0), flags
            var v2 = new byte[16];
            BitConverter.GetBytes(port).CopyTo(v2, 0);
            BitConverter.GetBytes(16u).CopyTo(v2, 4);
            BitConverter.GetBytes(7u).CopyTo(v2, 8);

            if (DeviceIoControl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, v2, 16, v2, 16, out _, 0))
            {
                var flags = BitConverter.ToUInt32(v2, 12);
                if ((flags & 0x4) != 0)
                    return "3.1";

                if ((flags & 0x1) != 0)
                    return "3.0";
            }

            // USB_NODE_CONNECTION_INFORMATION_EX: Speed is the byte after the 4-byte index, 18-byte descriptor and config value
            var ex = new byte[512];
            BitConverter.GetBytes(port).CopyTo(ex, 0);
            if (!DeviceIoControl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, ex, (uint)ex.Length, ex, (uint)ex.Length, out _, 0))
                return null;

            return ex[23] switch
            {
                0 or 1 => "1.1",
                2 => "2.0",
                _ => "3.0",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The present USB device node whose instance ID ends with the serial (<c>USB\VID_x&amp;PID_y\SERIAL</c>).</summary>
    private static uint? FindUsbDevice(string serial)
    {
        if (CM_Get_Device_ID_List_SizeW(out var length, "USB", CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
            return null;

        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW("USB", buffer, length, CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
            return null;

        var id = new string(buffer).Split('\0').FirstOrDefault(i => i.EndsWith($@"\{serial}", StringComparison.OrdinalIgnoreCase));
        if (id is null || CM_Locate_DevNodeW(out var devInst, id, 0) != CR_SUCCESS)
            return null;

        return devInst;
    }

    private static string? GetDeviceId(uint devInst)
    {
        if (CM_Get_Device_ID_Size(out var length, devInst, 0) != CR_SUCCESS)
            return null;

        var buffer = new char[length + 1];
        if (CM_Get_Device_IDW(devInst, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
            return null;

        return new string(buffer).TrimEnd('\0');
    }

    private static string? GetHubPath(string hubId)
    {
        var guid = GUID_DEVINTERFACE_USB_HUB;
        if (CM_Get_Device_Interface_List_SizeW(out var length, ref guid, hubId, 0) != CR_SUCCESS || length <= 1)
            return null;

        var buffer = new char[length];
        if (CM_Get_Device_Interface_ListW(ref guid, hubId, buffer, length, 0) != CR_SUCCESS)
            return null;

        return new string(buffer).Split('\0')[0];
    }

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property, out uint regDataType, ref uint buffer, ref uint length, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Get_Device_ID_Size(out uint length, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_IDW(uint devInst, [Out] char[] buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string deviceId, [Out] char[] buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, uint inLength, [Out] byte[] outBuffer, uint outLength, out uint returned, nint overlapped);
}
