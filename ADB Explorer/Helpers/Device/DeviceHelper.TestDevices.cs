#if DEBUG
namespace ADB_Explorer.Helpers;

public static partial class DeviceHelper
{
    private static int _testDeviceCounter = 0;

    private static LogicalDeviceViewModel MakeLogicalVM(string nameSuffix, string id, string ipAddress, DeviceType type, DeviceStatus status)
        => new(LogicalDevice.From(new DeviceSnapshot(id, nameSuffix, status, type, RootStatus.Unchecked, ipAddress, new()))) { IsTestDevice = true };

    private static IEnumerable<LogicalDeviceViewModel> CurrentLogical()
        => Data.DevicesObject.LogicalDeviceViewModels.ToList();

    private static IEnumerable<ServiceDeviceViewModel> CurrentServices()
        => Data.DevicesObject.ServiceDeviceViewModels.ToList();

    public static void TestDevices_AddLocal()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"USB Device {n}", $"TEST_USB_{n}", "", DeviceType.Local, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddRemote()
    {
        int n = ++_testDeviceCounter;
        var ip = $"192.168.{n / 256}.{n % 256}";
        var vm = MakeLogicalVM($"Wi-Fi Device {n}", $"{ip}:5555", ip, DeviceType.Remote, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddEmulator()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"emulator-{5554 + n}", $"emulator-{5554 + n}", "", DeviceType.Emulator, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddRecovery()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Recovery Device {n}", $"TEST_RECOVERY_{n}", "", DeviceType.Recovery, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddUnauthorized()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Unauthorized Device {n}", $"TEST_UNAUTH_{n}", "", DeviceType.Local, DeviceStatus.Unauthorized);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddOffline()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Offline Device {n}", $"TEST_OFFLINE_{n}", "", DeviceType.Local, DeviceStatus.Offline);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddPairingService()
    {
        int n = ++_testDeviceCounter;
        var ip = $"10.0.{n / 256}.{n % 256}";
        var svc = new ServiceDeviceViewModel(new ServiceDevice($"test-code-{n}_adb-tls-pairing._tcp.", ip, $"{5555 + n}", ServiceConnectionKind.Pairing)
        {
            MdnsType = ServiceDevice.PairingMode.PairingCode
        }) { IsTestDevice = true };
        Data.DevicesObject.UpdateServices([.. CurrentServices(), svc]);
    }

    public static void TestDevices_AddQrService()
    {
        int n = ++_testDeviceCounter;
        var ip = $"10.1.{n / 256}.{n % 256}";
        var svc = new ServiceDeviceViewModel(new ServiceDevice($"ADB_WIFI_QR_{n}_adb-tls-pairing._tcp.", ip, $"{5555 + n}", ServiceConnectionKind.Pairing)
        {
            MdnsType = ServiceDevice.PairingMode.QrCode
        }) { IsTestDevice = true };
        Data.DevicesObject.UpdateServices([.. CurrentServices(), svc]);
    }

    public static void TestDevices_Clear()
    {
        _testDeviceCounter = 0;
        Data.DevicesObject.UpdateDevices([]);
        Data.DevicesObject.UpdateServices([]);
    }
}
#endif
