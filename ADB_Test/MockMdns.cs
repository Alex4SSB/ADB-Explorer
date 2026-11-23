#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ADB_Test;

/// <summary>A service for scripts/mock-mdns.py; with <see cref="Target"/> the mock also relays its port there.</summary>
public record MockService(
    string Type,
    string Name,
    int Port,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Target = null);

/// <summary>Runs scripts/mock-mdns.py in control-file mode; <see cref="Set"/> changes what it advertises.</summary>
public sealed class MockMdns : IDisposable
{
    public const string PairingType = "_adb-tls-pairing._tcp";
    public const string ConnectType = "_adb-tls-connect._tcp";

    private static readonly JsonSerializerOptions ControlJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _controlFile = Path.Combine(Path.GetTempPath(), $"mock-mdns-{Guid.NewGuid():N}.json");
    private readonly Process _process;

    private MockMdns(string script, IEnumerable<MockService> services, Action<string> log)
    {
        Set(services);

        var info = new ProcessStartInfo("python", ["-u", script, "--control", _controlFile])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = Process.Start(info)!;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) log($"mock: {e.Data}"); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log($"mock error: {e.Data}"); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>Starts the mock, or marks the test inconclusive when python or the script is missing.</summary>
    public static MockMdns Start(Action<string> log, params MockService[] services)
    {
        var script = FindScript();
        if (script is null)
            Assert.Inconclusive("scripts/mock-mdns.py not found.");

        LanIp();

        try
        {
            return new MockMdns(script, services, log);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Inconclusive("python is not on PATH.");
            throw;
        }
    }

    public void Set(IEnumerable<MockService> services)
    {
        // Write then move, so the mock never reads a half-written file
        var temp = _controlFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(services, ControlJson));
        File.Move(temp, _controlFile, true);
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            // An empty list makes the mock send goodbyes, so the next test starts without stale services
            Set([]);
            Thread.Sleep(500);
            _process.Kill();
        }

        _process.Dispose();
        if (File.Exists(_controlFile))
            File.Delete(_controlFile);
    }

    public static string UniqueName() => $"adb-T{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}-AbCdEf";

    /// <summary>The IPv4 address multicast goes out on; marks the test inconclusive without a network.</summary>
    public static string LanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("10.255.255.255", 1);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch (SocketException)
        {
            Assert.Inconclusive("No IPv4 network to advertise mDNS services on.");
            throw;
        }
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "mock-mdns.py");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
