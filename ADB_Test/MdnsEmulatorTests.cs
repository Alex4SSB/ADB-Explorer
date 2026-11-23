#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

// Cooperative cancellation would make the app's adb calls return empty output instead of timing out
#pragma warning disable MSTEST0045

namespace ADB_Test;

/// <summary>
/// Real pairing and TLS against a throwaway emulator, reached through the mock's advertised relays.
/// Opt in with ADB_EXPLORER_EMULATOR_TESTS=1. Uses its own adb server, leaving the default one alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MdnsEmulatorTests
{
    // Not 5040: Windows' Connected Devices Platform service listens there
    private const string ServerPort = "5042";
    private const string InitialVersion = "platform-tools_37.0.1";

    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DropTimeout = TimeSpan.FromSeconds(10);

    private static TestEmulator? _emulator;
    private static string? _serverVersion;
    private static string? _emulatorSkipReason;
    private static bool _paired;

    private static string? _previousServerPort;
    private static string? _previousMaxPort;
    private static bool _usesOwnServer;

    private MockMdns? _mock;
    private readonly List<string> _connected = [];

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_EMULATOR_TESTS") != "1")
        {
            _emulatorSkipReason = "Set ADB_EXPLORER_EMULATOR_TESTS=1 to run the emulator tests.";
            return;
        }

        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };

        try
        {
            var adb = TestPaths.Adb(InitialVersion);
            UseOwnServer();

            AdbService.VerifyAdbVersion(adb);
            Assert.AreEqual(AdbHelper.AdbStatus.Valid, AdbHelper.CurrentAdbState.Status, $"{InitialVersion} was rejected");

            AdbService.KillAdbServer(restart: true);
            _serverVersion = InitialVersion;

            _emulator = TestEmulator.Boot();
            _emulator.SetWirelessDebugging(true);
            _emulator.WarmUpSettings();
        }
        catch (AssertInconclusiveException ex)
        {
            // Inconclusive in ClassInitialize doesn't reach the tests, so each test skips with this reason
            _emulatorSkipReason = ex.Message;
            Release();
        }
        catch
        {
            Release();
            throw;
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => Release();

    private static void UseOwnServer()
    {
        _previousServerPort = Environment.GetEnvironmentVariable("ANDROID_ADB_SERVER_PORT");
        _previousMaxPort = Environment.GetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT");

        // The emulator inherits the port; not scanning keeps this server off the user's emulators
        Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", ServerPort);
        Environment.SetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT", "5554");
        _usesOwnServer = true;
    }

    /// <summary>Shuts down what the class started. Never reaches the default server.</summary>
    private static void Release()
    {
        if (!_usesOwnServer)
            return;

        try
        {
            _emulator?.Dispose();
        }
        finally
        {
            _emulator = null;
            AdbService.KillAdbServer();

            Environment.SetEnvironmentVariable("ANDROID_ADB_SERVER_PORT", _previousServerPort);
            Environment.SetEnvironmentVariable("ADB_LOCAL_TRANSPORT_MAX_PORT", _previousMaxPort);
            _usesOwnServer = false;
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        _mock?.Dispose();
        _emulator?.CloseDialogs();

        foreach (var transport in _connected)
            AdbService.ExecuteAdbCommand("disconnect", out _, out _, default, transport);
    }

    // ── Pairing ─────────────────────────────────────────────────────────

    [TestMethod]
    [Timeout(32000)] // slowest measured run + 10%
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void PairWithCode_ThenConnect(string version)
    {
        var emulator = Start(version);

        var (code, pairingPort) = emulator.StartCodePairing();
        AdbService.PairNetworkDevice($"127.0.0.1:{emulator.Forward(pairingPort)}", code, TestContext.CancellationToken);

        emulator.CloseDialogs();
        var address = $"127.0.0.1:{emulator.Forward(emulator.OpenWirelessDebugging())}";
        Connect(address);

        Assert.AreEqual("1", RunOn(address, "getprop sys.boot_completed"));
    }

    [TestMethod]
    [Timeout(19000)] // slowest measured run + 10%
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void PairWithWrongCode_Throws(string version)
    {
        var emulator = Start(version);

        var (code, pairingPort) = emulator.StartCodePairing();
        var wrong = code == "000000" ? "111111" : "000000";

        Assert.ThrowsExactly<Exception>(() => AdbService.PairNetworkDevice($"127.0.0.1:{emulator.Forward(pairingPort)}", wrong, TestContext.CancellationToken));
    }

    [TestMethod]
    [Timeout(20000)] // slowest measured run + 10%
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void PairDiscoveredService_AppFlowSucceeds(string version)
    {
        var emulator = Start(version);

        var (code, pairingPort) = emulator.StartCodePairing();
        var name = $"adb-{emulator.Serial.Replace("-", "")}-PairTest";
        var relayPort = MockMdns.FreePort();
        _mock = MockMdns.Start(TestContext.WriteLine,
            new MockService(MockMdns.PairingType, name, relayPort, $"127.0.0.1:{emulator.Forward(pairingPort)}"));

        WaitForService(name, ServiceConnectionKind.Pairing);
        ApplyServices();
        var service = Data.DevicesObject.AllServices.Single(s => s.ID == name && s.ConnectionKind is ServiceConnectionKind.Pairing);
        Assert.AreEqual(ServiceDevice.PairingMode.PairingCode, service.MdnsType);

        // What the pairing code box on the service's card does
        service.PairingCode = code;
        Assert.IsTrue(DeviceHelper.PairService(service, TestContext.CancellationToken).Result);
    }

    // ── Connect and reconnect ───────────────────────────────────────────

    [TestMethod]
    [Timeout(22000)] // slowest measured run + 10%
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void ConnectByMdnsName_RunsCommands(string version)
    {
        var emulator = Start(version);
        EnsurePaired(emulator);

        var name = MockMdns.UniqueName();
        _mock = MockMdns.Start(TestContext.WriteLine, ConnectService(emulator, name, MockMdns.FreePort()));
        WaitForService(name, ServiceConnectionKind.Connect);

        var transport = $"{name}.{MockMdns.ConnectType}";
        Connect(transport);

        Assert.AreEqual(DeviceType.Service, DeviceHelper.GetType(transport, "device"));
        Assert.AreEqual("1", RunOn(transport, "getprop sys.boot_completed"));
    }

    [TestMethod]
    [Timeout(22000)] // slowest measured run + 10%
    [DataRow("platform-tools_36.0.2")]
    [DataRow("platform-tools_37.0.1")]
    public void Reconnect_AfterWirelessDebuggingRestart(string version)
    {
        var emulator = Start(version);
        EnsurePaired(emulator);

        var name = MockMdns.UniqueName();
        var transport = $"{name}.{MockMdns.ConnectType}";
        _mock = MockMdns.Start(TestContext.WriteLine, ConnectService(emulator, name, MockMdns.FreePort()));
        WaitForService(name, ServiceConnectionKind.Connect);
        Connect(transport);
        Assert.AreEqual("1", RunOn(transport, "getprop sys.boot_completed"));

        // Turning Wireless debugging off closes the TLS connection, as leaving the network does
        emulator.SetWirelessDebugging(false);
        _mock.Set([]);
        Assert.IsTrue(TestEmulator.WaitUntil(() => TransportState(transport) is not "device", DropTimeout),
            "The connection stayed up after Wireless debugging was turned off");

        // Back on: adbd listens on a new port, and the service is advertised again
        emulator.SetWirelessDebugging(true);
        _mock.Set([ConnectService(emulator, name, MockMdns.FreePort())]);
        WaitForService(name, ServiceConnectionKind.Connect);

        // adb only auto-connects to GUID names, which the mock can't use, so this reconnects as the user would
        Connect(transport);

        Assert.AreEqual("1", RunOn(transport, "getprop sys.boot_completed"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private TestEmulator Start(string version)
    {
        if (_emulatorSkipReason is not null)
            Assert.Inconclusive(_emulatorSkipReason);

        var adb = TestPaths.Adb(version);

        Data.DevicesObject = new Devices();
        Data.DevicesObject.UIList.Clear();

        AdbService.VerifyAdbVersion(adb);
        Assert.AreEqual(AdbHelper.AdbStatus.Valid, AdbHelper.CurrentAdbState.Status, $"{version} was rejected");

        // A client of another version would restart the server anyway; doing it here waits for the emulator to return
        if (_serverVersion != version)
        {
            AdbService.KillAdbServer(restart: true);
            _serverVersion = version;
        }

        if (!TestEmulator.WaitUntil(_emulator!.Attach, DiscoveryTimeout))
            Assert.Inconclusive($"{_emulator.Serial} didn't attach to the {version} server.");

        if (!AdbService.CheckMDNS())
            Assert.Inconclusive($"{version} has no mDNS backend running.");

        return _emulator;
    }

    private void Connect(string transport)
    {
        _connected.Add(transport);
        AdbService.ConnectNetworkDevice(transport, TestContext.CancellationToken);
    }

    /// <summary>Pairs once per run.</summary>
    private static void EnsurePaired(TestEmulator emulator)
    {
        if (_paired)
            return;

        emulator.Pair();
        _paired = true;
    }

    /// <summary>A connect service relayed from port to the emulator's current TLS port.</summary>
    /// <remarks>Not named by the GUID: the emulator's own record, with its NAT address, reaches the host too.</remarks>
    private static MockService ConnectService(TestEmulator emulator, string name, int port)
    {
        var tlsPort = emulator.OpenWirelessDebugging();
        emulator.CloseDialogs();

        return new MockService(MockMdns.ConnectType, name, port, $"127.0.0.1:{emulator.Forward(tlsPort)}");
    }

    private void WaitForService(string name, ServiceConnectionKind kind)
    {
        var seen = TestEmulator.WaitUntil(
            () => WiFiPairingService.GetServices(TestContext.CancellationToken).Any(s => s.ID == name && s.ConnectionKind == kind),
            DiscoveryTimeout);

        Assert.IsTrue(seen, $"{name} ({kind}) was not discovered within {DiscoveryTimeout.TotalSeconds} s");
    }

    private void ApplyServices()
    {
        var snapshots = WiFiPairingService.GetServices(TestContext.CancellationToken).ToList();
        Data.DevicesObject.UpdateServices(snapshots.Select(s => new ServiceDeviceViewModel(ServiceDevice.From(s))));
    }

    private string RunOn(string serial, string command)
    {
        // A TLS transport is listed as connecting for a moment after adb connect returns
        if (!TestEmulator.WaitUntil(() => TransportState(serial) is "device", DiscoveryTimeout))
        {
            AdbService.ExecuteAdbCommand("devices", out var devices, out _, TestContext.CancellationToken, "-l");
            Assert.Fail($"{serial} is {TransportState(serial) ?? "not listed"}, not online. adb devices:\n{devices}");
        }

        AdbService.ExecuteDeviceAdbCommand(serial, "shell", out var stdout, out _, TestContext.CancellationToken, command);
        return stdout.Trim();
    }

    private string? TransportState(string serial)
    {
        AdbService.ExecuteAdbCommand("devices", out var stdout, out _, TestContext.CancellationToken);

        return stdout.Split('\n')
            .Select(l => l.Trim().Split('\t'))
            .Where(p => p.Length == 2 && p[0] == serial)
            .Select(p => p[1])
            .FirstOrDefault();
    }
}
