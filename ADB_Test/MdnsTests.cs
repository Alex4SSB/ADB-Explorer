#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace ADB_Test;

/// <summary>Parsing and bookkeeping of <c>adb mdns services</c> output, without adb.</summary>
[TestClass]
public class MdnsTests
{
    // The output format of adb 36.0.2 (Openscreen) and 37.0.1 ("adb discovery"); both use tabs and CRLF.
    private const string Output36 =
        "List of discovered mdns services\r\n" +
        "adb-SERIALABC-QXjCrW\t_adb-tls-connect._tcp\t192.0.2.92:34193\r\n" +
        "adb-SERIALABC-QXjCrW\t_adb-tls-pairing._tcp\t192.0.2.92:41011\r\n\r\n";

    private const string Output37 =
        "List of discovered mdns services\r\n" +
        "adb-MOCK0002-AbCdEf\t_adb-tls-pairing._tcp\t192.0.2.186:37001\r\n" +
        "adb-MOCK0001-AbCdEf\t_adb-tls-connect._tcp\t192.0.2.186:37000\r\n\r\n";

    // Older adb versions printed a trailing dot after the service type.
    private const string OutputTrailingDot =
        "List of discovered mdns services\n" +
        "adb-SERIALXYZ-AbCdEf\t_adb-tls-connect._tcp.\t10.0.0.5:40000\n";

    private static ServiceSnapshot[] Parse(string output)
        => [.. AdbRegEx.RE_MDNS_SERVICE().Matches(output).Select(ServiceSnapshot.Parse).Where(s => s)];

    [TestInitialize]
    public void Setup()
    {
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        Data.DevicesObject = new Devices();
        Data.DevicesObject.UIList.Clear();
    }

    // ── Parsing ─────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(Output36, DisplayName = "adb 36")]
    [DataRow(Output37, DisplayName = "adb 37")]
    public void Parse_ListsConnectAndPairingServices(string output)
    {
        var services = Parse(output);

        Assert.HasCount(2, services);
        Assert.AreEqual(1, services.Count(s => s.ConnectionKind is ServiceConnectionKind.Connect));
        Assert.AreEqual(1, services.Count(s => s.ConnectionKind is ServiceConnectionKind.Pairing));
    }

    [TestMethod]
    public void Parse_ReadsIdAddressAndPort()
    {
        var pairing = Parse(Output37).Single(s => s.ConnectionKind is ServiceConnectionKind.Pairing);

        Assert.AreEqual("adb-MOCK0002-AbCdEf", pairing.ID);
        Assert.AreEqual("192.0.2.186", pairing.IpAddress);
        Assert.AreEqual("37001", pairing.Port);
    }

    [TestMethod]
    public void Parse_AcceptsTrailingDotAfterType()
    {
        var service = Parse(OutputTrailingDot).Single();

        Assert.AreEqual("adb-SERIALXYZ-AbCdEf", service.ID);
        Assert.AreEqual("40000", service.Port);
    }

    [TestMethod]
    public void Parse_NoServices_Empty()
    {
        Assert.IsEmpty(Parse("List of discovered mdns services\r\n\r\n"));
        Assert.IsEmpty(Parse(""));
    }

    [TestMethod]
    public void Parse_IgnoresNonAdbServiceTypes()
    {
        var output = "List of discovered mdns services\n"
                     + "emulator-mock\t_adb._tcp\t192.168.1.2:5555\n";

        Assert.IsEmpty(Parse(output));
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("0.0.0.0")]
    public void Parse_LoopbackServiceIgnored(string address)
    {
        var output = $"adb-WSA-xyz\t_adb-tls-pairing._tcp\t{address}:58526\n";

        Assert.IsEmpty(Parse(output));
    }

    // ── Pairing mode ────────────────────────────────────────────────────

    [TestMethod]
    public void QrServiceName_ParsedAsQrCode()
    {
        var name = AdbExplorerConst.PAIRING_SERVICE_PREFIX + "Ab3dEf7hIj";
        var service = Parse($"{name}\t_adb-tls-pairing._tcp\t192.168.1.40:37123\n").Single();

        Assert.AreEqual(ServiceDevice.PairingMode.QrCode, service.MdnsType);
        Assert.AreEqual(ServiceConnectionKind.Pairing, service.ConnectionKind);
    }

    [TestMethod]
    public void DeviceServiceName_ParsedAsPairingCode()
    {
        var service = Parse(Output37).First(s => s.ConnectionKind is ServiceConnectionKind.Pairing);

        Assert.AreEqual(ServiceDevice.PairingMode.PairingCode, service.MdnsType);
    }

    [TestMethod]
    public void QrPairingString_HasAdbWifiFormat()
    {
        Assert.AreEqual("WIFI:T:ADB;S:adbexplorer-abc;P:secret;;",
            WiFiPairingService.CreatePairingString("adbexplorer-abc", "secret"));
    }

    // ── Serial and type ─────────────────────────────────────────────────

    [TestMethod]
    [DataRow("adb-SERIALABC-QXjCrW", "SERIALABC")]
    [DataRow("adb-SERIALABC-QXjCrW._adb-tls-connect._tcp", "SERIALABC")]
    [DataRow("adb-SERIAL-XYZ-xYz123", "SERIAL-XYZ")]
    [DataRow("adbexplorer-Ab3dEf7hIj", "adbexplorer-Ab3dEf7hIj")]
    public void ParseMdnsSerial_StripsPrefixAndSuffix(string id, string serial)
    {
        Assert.AreEqual(serial, DeviceHelper.ParseMdnsSerial(id));
    }

    [TestMethod]
    public void MdnsTransport_IsServiceType()
    {
        Assert.AreEqual(DeviceType.Service, DeviceHelper.GetType("adb-SERIALABC-QXjCrW._adb-tls-connect._tcp", "device"));
    }

    // ── Change detection and re-advertising ─────────────────────────────

    [TestMethod]
    public void ServicesChanged_DetectsNewSameAndRemoved()
    {
        var services = Parse(Output37);

        Assert.IsTrue(Data.DevicesObject.ServicesChanged(services));

        Data.DevicesObject.UpdateServices(services.Select(ToViewModel));
        Assert.IsFalse(Data.DevicesObject.ServicesChanged(services));
        Assert.IsTrue(Data.DevicesObject.ServicesChanged(services.Take(1)));
        Assert.IsTrue(Data.DevicesObject.ServicesChanged([]));
    }

    [TestMethod]
    public void ServicesChanged_DetectsPortChange()
    {
        var before = Parse(Output37);
        Data.DevicesObject.UpdateServices(before.Select(ToViewModel));

        var after = Parse(Output37.Replace(":37001", ":37999"));

        Assert.IsTrue(Data.DevicesObject.ServicesChanged(after));
    }

    [TestMethod]
    public void ServicesChanged_NullSnapshot_NotAChange()
    {
        Assert.IsFalse(Data.DevicesObject.ServicesChanged(null!));
    }

    [TestMethod]
    public void Readvertised_KeepsServiceObjectAndPairingState()
    {
        Data.DevicesObject.UpdateServices(Parse(Output37).Select(ToViewModel));
        var pairing = Data.DevicesObject.AllServices.Single(s => s.ConnectionKind is ServiceConnectionKind.Pairing);
        pairing.PairingCode = "123456";

        // The device restarts wireless debugging: same name, new port
        Data.DevicesObject.UpdateServices(Parse(Output37.Replace(":37001", ":37999")).Select(ToViewModel));
        var updated = Data.DevicesObject.AllServices.Single(s => s.ConnectionKind is ServiceConnectionKind.Pairing);

        Assert.AreSame(pairing, updated);
        Assert.AreEqual("37999", updated.PairingPort);
        Assert.AreEqual("123456", updated.PairingCode);
    }

    [TestMethod]
    public void PairingAndConnectWithSameName_AreSeparateServices()
    {
        Data.DevicesObject.UpdateServices(Parse(Output36).Select(ToViewModel));

        Assert.HasCount(2, Data.DevicesObject.AllServices);
    }

    [TestMethod]
    public void RemovedService_LeavesDeviceList()
    {
        Data.DevicesObject.UpdateServices(Parse(Output37).Select(ToViewModel));
        Data.DevicesObject.UpdateServices([]);

        Assert.IsEmpty(Data.DevicesObject.AllServices);
        Assert.IsFalse(Data.DevicesObject.UIList.OfType<ServiceDeviceViewModel>().Any());
    }

    [TestMethod]
    public void ServiceOfOnlineDevice_IsOwnedNotListed()
    {
        var logical = new LogicalDeviceViewModel(LogicalDevice.From(new DeviceSnapshot(
            "192.0.2.186:37000", "Phone", DeviceStatus.Ok, DeviceType.Remote, RootStatus.Unchecked, "192.0.2.186", default!)));
        Data.DevicesObject.UIList.Add(logical);

        Data.DevicesObject.UpdateServices(Parse(Output37).Select(ToViewModel));

        Assert.HasCount(2, Data.DevicesObject.AllServices);
        Assert.IsFalse(Data.DevicesObject.UIList.OfType<ServiceDeviceViewModel>().Any());
    }

    // ── Pairing guards ──────────────────────────────────────────────────

    [TestMethod]
    public void PairService_NoCode_DoesNotPair()
    {
        var service = ToViewModel(Parse(Output37).First(s => s.ConnectionKind is ServiceConnectionKind.Pairing));

        Assert.IsFalse(DeviceHelper.PairService(service, default).Result);
        Assert.IsFalse(service.IsPairingInProgress);
    }

    [TestMethod]
    public void PairService_QrWithoutQrSession_DoesNotPair()
    {
        // No QR code has been shown, so there's no password to pair with
        Assert.IsNull(Data.MdnsService?.QrClass);
        var name = AdbExplorerConst.PAIRING_SERVICE_PREFIX + "Ab3dEf7hIj";
        var service = ToViewModel(Parse($"{name}\t_adb-tls-pairing._tcp\t192.168.1.40:37123\n").Single());

        Assert.IsFalse(DeviceHelper.PairService(service, default).Result);
    }

    private static ServiceDeviceViewModel ToViewModel(ServiceSnapshot s) => new(ServiceDevice.From(s));
}
