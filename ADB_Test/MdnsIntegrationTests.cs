#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Test;

/// <summary>
/// Runs the app's mDNS code against real adb versions, with scripts/mock-mdns.py advertising the services.
/// Uses its own adb server port, so devices on the default server are untouched.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MdnsIntegrationTests
{
    private const string ServerPort = "5039";
    private const string PairingType = MockMdns.PairingType;
    private const string ConnectType = MockMdns.ConnectType;

    // Discovery took under 3 s in manual runs; the PTR re-announce backs off to 30 s, so allow some slack.
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RemovalTimeout = TimeSpan.FromSeconds(6);

    private static string? _previousServerPort;
    private static string? _previousMaxPort;

    private MockMdns? _mock;
    private readonly List<TcpListener> _listeners = [];

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        _previousServerPort = Environment.GetEnvironmentVariable("ANDROID_ADB_SERVER_PORT");
        Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", ServerPort);

        _previousMaxPort = Environment.GetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT");

        // An emulator serves one server at a time; without this the side server takes it from the default one
        Environment.SetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT", "5554");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (!string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            AdbService.KillAdbServer();

        Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", _previousServerPort);
        Environment.SetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT", _previousMaxPort);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _mock?.Dispose();
        foreach (var listener in _listeners)
            listener.Stop();
    }

    // ── Discovery ───────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void MdnsCheck_ReportsRunning(string version)
    {
        StartAdb(version);

        Assert.IsTrue(AdbService.CheckMDNS());
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void PairingCodeService_Discovered(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        StartMock(new MockService(PairingType, name, 37101));

        var service = WaitForService(name, ServiceConnectionKind.Pairing);

        Assert.AreEqual(ServiceDevice.PairingMode.PairingCode, service.MdnsType);
        Assert.AreEqual("37101", service.Port);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void QrService_DiscoveredAsQr(string version)
    {
        StartAdbWithMdns(version);
        var name = AdbExplorerConst.PAIRING_SERVICE_PREFIX + Guid.NewGuid().ToString("N")[..10];
        StartMock(new MockService(PairingType, name, 37102));

        var service = WaitForService(name, ServiceConnectionKind.Pairing);

        Assert.AreEqual(ServiceDevice.PairingMode.QrCode, service.MdnsType);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void ConnectService_DiscoveredButNotListed(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        StartMock(new MockService(ConnectType, name, 37103), new MockService(PairingType, name, 37104));

        WaitForService(name, ServiceConnectionKind.Connect);
        WaitForService(name, ServiceConnectionKind.Pairing);
        ApplyServices();

        var listed = Data.DevicesObject.UIList.OfType<ServiceDeviceViewModel>()
            .Where(s => s.ID == name && DeviceHelper.EvaluateDevicePredicate(s, Data.DevicesObject))
            .ToList();

        Assert.HasCount(1, listed);
        Assert.AreEqual(ServiceConnectionKind.Pairing, listed[0].ConnectionKind);
    }

    // ── Pairing ─────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void ManualPairing_AdbReachesAdvertisedPort(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        var listener = Listen(out var port);
        StartMock(new MockService(PairingType, name, port));
        WaitForService(name, ServiceConnectionKind.Pairing);
        ApplyServices();

        var service = Data.DevicesObject.AllServices.Single(s => s.ID == name);
        service.PairingCode = "123456";
        var accepted = AcceptAsync(listener);

        // The mock can't complete SPAKE2, so pairing fails - after adb resolved the name and connected
        var paired = DeviceHelper.PairService(service, TestContext.CancellationToken).Result;

        Assert.IsTrue(accepted.Wait(DiscoveryTimeout), "adb never connected to the pairing port");
        // Pairing state isn't checked: App.SafeInvoke has no dispatcher in tests, so EndPairing never runs
        Assert.IsFalse(paired);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void FailedPairing_ThrowsWithAdbError(string version)
    {
        StartAdbWithMdns(version);
        var listener = Listen(out var port);
        var accepted = AcceptAsync(listener);

        var ex = Assert.ThrowsExactly<Exception>(() => AdbService.PairNetworkDevice($"{LanIp()}:{port}", "123456", TestContext.CancellationToken));

        Assert.StartsWith("error:", ex.Message);
        Assert.DoesNotContain("daemon", ex.Message);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void QrPairing_AdbPairsByServiceName(string version)
    {
        StartAdbWithMdns(version);
        var name = AdbExplorerConst.PAIRING_SERVICE_PREFIX + Guid.NewGuid().ToString("N")[..10];
        var listener = Listen(out var port);
        StartMock(new MockService(PairingType, name, port));
        WaitForService(name, ServiceConnectionKind.Pairing);
        var accepted = AcceptAsync(listener);

        // The same call ListServices makes once the phone advertises the QR's service name
        Assert.ThrowsExactly<Exception>(() => AdbService.PairNetworkDevice(name, "QrPassword12", TestContext.CancellationToken));
        Assert.IsTrue(accepted.Wait(DiscoveryTimeout), "adb never connected to the QR pairing port");
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void ManualPairing_ByAddress_ReachesPort(string version)
    {
        StartAdb(version);
        var listener = Listen(out var port);
        var accepted = AcceptAsync(listener);

        // Add New Device → pairing with an IP and port, no mDNS involved
        Assert.ThrowsExactly<Exception>(() => AdbService.PairNetworkDevice($"{LanIp()}:{port}", "123456", TestContext.CancellationToken));
        Assert.IsTrue(accepted.Wait(DiscoveryTimeout), "adb never connected to the pairing address");
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void Disconnect_DeviceAlreadyGone_Succeeds(string version)
    {
        StartAdb(version);

        // adb exits 1 with "error: no such device", but the caller's goal is met
        AdbService.DisconnectNetworkDevice("127.0.0.1:59999", TestContext.CancellationToken);
    }

    [TestMethod]
    [DataRow("platform-tools_37.0.1")]
    public void CancelledPairing_IsNotSuccess(string version)
    {
        StartAdb(version);
        var listener = Listen(out var port);

        // The listener never answers, so pairing is still running when it's canceled
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        Assert.Throws<OperationCanceledException>(() => AdbService.PairNetworkDevice($"{LanIp()}:{port}", "123456", cancellation.Token));
    }

    // ── Connect and reconnect ───────────────────────────────────────────

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void Connect_ByMdnsName_SendsAdbHandshake(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        var listener = Listen(out var port);
        StartMock(new MockService(ConnectType, name, port));
        WaitForService(name, ServiceConnectionKind.Connect);

        var handshake = ReadHandshakeAsync(listener);
        TryConnect($"{name}.{ConnectType}");

        Assert.IsTrue(handshake.Wait(DiscoveryTimeout), "adb never connected to the advertised port");
        Assert.AreEqual("CNXN", handshake.Result);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void Reconnect_ServiceGoneThenBackOnNewPort(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        var first = Listen(out var firstPort);
        var second = Listen(out var secondPort);
        StartMock(new MockService(ConnectType, name, firstPort));
        WaitForService(name, ServiceConnectionKind.Connect);

        // Wireless debugging turned off: the service says goodbye
        WriteControl([]);
        Assert.IsTrue(WaitUntil(() => FindService(name, ServiceConnectionKind.Connect) is null, RemovalTimeout),
            "adb kept the service after its goodbye");

        // Turned on again, on a new port, as Android does
        WriteControl([new MockService(ConnectType, name, secondPort)]);
        var service = WaitForService(name, ServiceConnectionKind.Connect);
        Assert.AreEqual(secondPort.ToString(), service.Port);

        var oldHandshake = ReadHandshakeAsync(first);
        var newHandshake = ReadHandshakeAsync(second);
        TryConnect($"{name}.{ConnectType}");

        Assert.IsTrue(newHandshake.Wait(DiscoveryTimeout), "adb didn't reconnect on the new port");
        Assert.IsFalse(oldHandshake.IsCompleted, "adb connected to the stale port");
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void Reconnect_PairingPortChange_UpdatesExistingService(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        StartMock(new MockService(PairingType, name, 37105));
        WaitForService(name, ServiceConnectionKind.Pairing);
        ApplyServices();

        var service = Data.DevicesObject.AllServices.Single(s => s.ID == name);
        service.PairingCode = "654321";

        WriteControl([new MockService(PairingType, name, 37106)]);
        Assert.IsTrue(WaitUntil(() => FindService(name, ServiceConnectionKind.Pairing)?.Port == "37106", DiscoveryTimeout),
            "adb never saw the new pairing port");
        ApplyServices();

        var updated = Data.DevicesObject.AllServices.Single(s => s.ID == name);
        Assert.AreSame(service, updated);
        Assert.AreEqual("37106", updated.PairingPort);
        Assert.AreEqual("654321", updated.PairingCode);
    }

    [TestMethod]
    [DataRow("platform-tools_35.0.2")]
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void AdbRestart_RediscoversServices(string version)
    {
        StartAdbWithMdns(version);
        var name = UniqueName();
        StartMock(new MockService(PairingType, name, 37107));
        WaitForService(name, ServiceConnectionKind.Pairing);

        // MdnsService.Restart does the same
        AdbService.KillAdbServer(restart: true);

        Assert.IsTrue(AdbService.CheckMDNS());
        WaitForService(name, ServiceConnectionKind.Pairing);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string UniqueName() => MockMdns.UniqueName();

    private static string LanIp() => MockMdns.LanIp();

    private static void StartAdb(string version)
    {
        var adb = TestPaths.Adb(version);

        Data.DevicesObject = new Devices();
        Data.DevicesObject.UIList.Clear();

        AdbService.VerifyAdbVersion(adb);
        Assert.AreEqual(AdbHelper.AdbStatus.Valid, AdbHelper.CurrentAdbState.Status, $"{version} was rejected");

        // A fresh server per test, so no services are cached from a previous one
        AdbService.KillAdbServer(restart: true);
    }

    /// <summary><see cref="StartAdb"/>, skipping when mDNS isn't running; MdnsCheck_ReportsRunning is what fails for that.</summary>
    private static void StartAdbWithMdns(string version)
    {
        StartAdb(version);

        if (!AdbService.CheckMDNS())
            Assert.Inconclusive($"{version} has no mDNS backend running (see MdnsCheck_ReportsRunning).");
    }

    private void StartMock(params MockService[] services) => _mock = MockMdns.Start(TestContext.WriteLine, services);

    private void WriteControl(IEnumerable<MockService> services) => _mock!.Set(services);

    private ServiceSnapshot? FindService(string name, ServiceConnectionKind kind)
    {
        var services = WiFiPairingService.GetServices(TestContext.CancellationToken);

        return services.Where(s => s.ID == name && s.ConnectionKind == kind).Cast<ServiceSnapshot?>().FirstOrDefault();
    }

    private ServiceSnapshot WaitForService(string name, ServiceConnectionKind kind)
    {
        ServiceSnapshot? found = null;
        var seen = WaitUntil(() => (found = FindService(name, kind)) is not null, DiscoveryTimeout);

        Assert.IsTrue(seen, $"{name} ({kind}) was not discovered within {DiscoveryTimeout.TotalSeconds} s");
        return found!.Value;
    }

    private void ApplyServices()
    {
        var snapshots = WiFiPairingService.GetServices(TestContext.CancellationToken).ToList();
        Data.DevicesObject.UpdateServices(snapshots.Select(s => new ServiceDeviceViewModel(ServiceDevice.From(s))));
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(250);
        }

        return condition();
    }

    private TcpListener Listen(out int port)
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        _listeners.Add(listener);
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static Task AcceptAsync(TcpListener listener) => Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
    });

    private static Task<string> ReadHandshakeAsync(TcpListener listener) => Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        var buffer = new byte[4];
        var stream = client.GetStream();
        stream.ReadTimeout = 3000;

        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read));
            if (n == 0)
                break;

            read += n;
        }

        return System.Text.Encoding.ASCII.GetString(buffer, 0, read);
    });

    private void TryConnect(string address)
    {
        // Fails once the mock closes the socket; the listener is what proves the connection
        try
        {
            AdbService.ConnectNetworkDevice(address, TestContext.CancellationToken);
        }
        catch (Exception) { }
    }
}
