#nullable enable

using ADB_Explorer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace ADB_Test;

/// <summary>
/// A throwaway Android 11+ emulator for Wireless debugging tests, on ports outside the 5555-5585 adb scans.
/// Only the test's adb server knows of it. Host access to its adbd goes through <see cref="Forward"/>.
/// </summary>
public sealed partial class TestEmulator : IDisposable
{
    // Boot took 25 s here from a cold start; a first boot after creating the AVD can take minutes.
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(25);

    // Wireless debugging stuck about 10 s after Wi-Fi connected on a fresh boot.
    private static readonly TimeSpan WifiSettleTimeout = TimeSpan.FromSeconds(30);

    // The slowest adb call measured is pairing, at about 3 s.
    private static readonly TimeSpan AdbCommandTimeout = TimeSpan.FromSeconds(20);

    private const int ConsolePort = 5640;
    private const int AdbPort = ConsolePort + 1;

    private readonly Process _process;

    // The client must match the server's version, or adb restarts the server
    private static string Adb => AdbHelper.CurrentAdbState.Path;

    public string Serial { get; } = $"emulator-{ConsolePort}";

    private TestEmulator(Process process) => _process = process;

    /// <summary>Boots a throwaway instance of an Android 11+ AVD; marks the test inconclusive when it can't.</summary>
    public static TestEmulator Boot()
    {
        var emulatorExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\emulator\emulator.exe");
        var avd = Environment.GetEnvironmentVariable("ADB_EXPLORER_TEST_AVD") ?? FindAvd();
        if (!File.Exists(emulatorExe) || avd is null)
            Assert.Inconclusive("No Android emulator with an Android 11+ AVD to boot.");

        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        if (listeners.Any(l => l.Port is ConsolePort or AdbPort))
            Assert.Inconclusive($"Port {ConsolePort} or {AdbPort} is in use, perhaps by an earlier test emulator.");

        // -read-only discards what the tests change in the AVD
        var process = Process.Start(new ProcessStartInfo(emulatorExe,
            ["-avd", avd, "-read-only", "-ports", $"{ConsolePort},{AdbPort}", "-no-window", "-no-boot-anim", "-no-snapshot-save", "-no-audio"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var emulator = new TestEmulator(process);
        try
        {
            var booted = WaitUntil(() => process.HasExited || emulator.IsBooted(), BootTimeout);
            if (process.HasExited)
                Assert.Inconclusive($"{avd} exited while booting; it may be open in another emulator.");

            if (!booted)
                Assert.Inconclusive($"{avd} didn't boot within {BootTimeout.TotalSeconds} s.");

            return Prepare(emulator);
        }
        catch
        {
            emulator.Dispose();
            throw;
        }
    }

    /// <summary>Registers the emulator with the current server, which doesn't scan its ports.</summary>
    public bool Attach()
    {
        if (IsOnline())
            return true;

        RunAdb(Adb, "connect", $"emu:{ConsolePort},{AdbPort}");
        return IsOnline();
    }

    private bool IsOnline()
        => RunAdb(Adb, "devices").Split('\n').Any(l => l.Trim() == $"{Serial}\tdevice");

    private bool IsBooted() => Attach() && Shell("getprop sys.boot_completed").Trim() == "1";

    private static TestEmulator Prepare(TestEmulator emulator)
    {
        var sdk = int.TryParse(emulator.Shell("getprop ro.build.version.sdk").Trim(), out var level) ? level : 0;
        if (sdk < 30)
            Assert.Inconclusive($"{emulator.Serial} runs API {sdk}; Wireless debugging needs 30+.");

        return emulator;
    }

    public void Dispose()
    {
        try
        {
            if (_process.HasExited)
                return;

            RunAdb(Adb, "-s", Serial, "emu", "kill");
        }
        finally
        {
            // Covers a boot that timed out, which emu kill can't reach
            if (!_process.WaitForExit(ShutdownTimeout))
                _process.Kill(entireProcessTree: true);

            _process.Dispose();
        }
    }

    // ── Wireless debugging ──────────────────────────────────────────────

    /// <summary>Pairs through a forward with adb directly.</summary>
    public void Pair()
    {
        var (code, pairingPort) = StartCodePairing();
        var output = RunAdb(Adb, "pair", $"127.0.0.1:{Forward(pairingPort)}", code);
        CloseDialogs();

        Assert.IsTrue(GuidRegex().IsMatch(output), $"Pairing failed: {output}");
    }

    public void SetWirelessDebugging(bool enabled)
    {
        Shell("settings put global development_settings_enabled 1");

        if (!enabled)
        {
            Shell("settings put global adb_wifi_enabled 0");
            return;
        }

        // Right after boot Wi-Fi isn't connected yet, and Android turns Wireless debugging back off
        Assert.IsTrue(WaitUntil(() => Shell("cmd wifi status").Contains("Wifi is connected"), BootTimeout),
            "The emulator's Wi-Fi never connected");

        // Even then it's reset to 0 for a few seconds after boot, so set it until it sticks
        var deadline = DateTime.UtcNow + WifiSettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            Shell("settings put global adb_wifi_enabled 1");

            // Android asks on each throwaway boot. The prompt closes if Wi-Fi changes, so the next round retries
            if (WaitForText("Always allow on this network", TimeSpan.FromSeconds(2)) is not null
                && TryTap("Always allow on this network"))
            {
                TryTap("Allow");
            }

            if (WaitUntil(() => Shell("settings get global adb_wifi_enabled").Trim() == "1", TimeSpan.FromSeconds(2)))
                return;
        }

        Assert.Fail($"Wireless debugging didn't stay on within {WifiSettleTimeout.TotalSeconds} s");
    }

    /// <summary>Opens Developer options → Wireless debugging and reads the TLS (connect) port it shows.</summary>
    public int OpenWirelessDebugging()
    {
        Shell("input keyevent KEYCODE_HOME");
        Shell("am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS");
        TapScrolling("Wireless debugging");

        var address = WaitForText(AddressRegex(), UiTimeout);
        Assert.IsNotNull(address, "Wireless debugging page shows no IP address & port");

        return int.Parse(AddressRegex().Match(address).Groups["port"].Value);
    }

    /// <summary>Opens the pairing dialog once, as Settings is several times slower the first time after boot.</summary>
    public void WarmUpSettings()
    {
        try
        {
            StartCodePairing();
        }
        catch (AssertFailedException)
        {
            // Too slow while cold; the tests repeat these steps and report a real failure
        }

        CloseDialogs();
    }

    /// <summary>Taps "Pair device with pairing code" on the Wireless debugging page and reads the code and pairing port.</summary>
    public (string Code, int Port) StartCodePairing()
    {
        var connectPort = OpenWirelessDebugging();
        Tap("Pair device with pairing code");

        string? code = null;
        int port = 0;
        var shown = WaitUntil(() =>
        {
            var texts = Texts();
            code = texts.FirstOrDefault(t => CodeRegex().IsMatch(t));
            var pairing = texts.Select(t => AddressRegex().Match(t)).FirstOrDefault(m => m.Success && int.Parse(m.Groups["port"].Value) != connectPort);
            port = pairing is null ? 0 : int.Parse(pairing.Groups["port"].Value);
            return code is not null && port != 0;
        }, UiTimeout);

        Assert.IsTrue(shown, "The pairing dialog didn't show a code and port");
        return (code!, port);
    }

    public void CloseDialogs()
    {
        Shell("input keyevent KEYCODE_BACK");
        Shell("input keyevent KEYCODE_HOME");
    }

    /// <summary>Forwards a free host port to an emulator port and returns the host port.</summary>
    public int Forward(int devicePort)
    {
        var output = RunAdb(Adb, "-s", Serial, "forward", "tcp:0", $"tcp:{devicePort}").Trim();
        return int.Parse(output);
    }

    // ── UI ──────────────────────────────────────────────────────────────

    private record UiNode(string Text, int X, int Y);

    private List<UiNode> Dump()
    {
        var xml = RunAdb(Adb, "-s", Serial, "exec-out", "uiautomator", "dump", "/dev/tty");
        var start = xml.IndexOf("<?xml", StringComparison.Ordinal);
        var end = xml.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (start < 0 || end < 0)
            return [];

        var root = XDocument.Parse(xml[start..(end + "</hierarchy>".Length)]);
        List<UiNode> nodes = [];
        foreach (var node in root.Descendants("node"))
        {
            var text = (string?)node.Attribute("text");
            if (string.IsNullOrEmpty(text))
                text = (string?)node.Attribute("content-desc");

            var bounds = BoundsRegex().Match((string?)node.Attribute("bounds") ?? "");
            if (string.IsNullOrEmpty(text) || !bounds.Success)
                continue;

            var b = bounds.Groups.Values.Skip(1).Select(g => int.Parse(g.Value)).ToArray();
            nodes.Add(new(text, (b[0] + b[2]) / 2, (b[1] + b[3]) / 2));
        }

        return nodes;
    }

    private List<string> Texts() => [.. Dump().Select(n => n.Text)];

    private string? WaitForText(string text, TimeSpan timeout)
    {
        string? found = null;
        WaitUntil(() => (found = Texts().FirstOrDefault(t => t.Equals(text, StringComparison.OrdinalIgnoreCase))) is not null, timeout);
        return found;
    }

    private string? WaitForText(Regex pattern, TimeSpan timeout)
    {
        string? found = null;
        WaitUntil(() => (found = Texts().FirstOrDefault(pattern.IsMatch)) is not null, timeout);
        return found;
    }

    private void Tap(string text)
    {
        UiNode? node = null;
        var found = WaitUntil(() => (node = Dump().FirstOrDefault(n => n.Text.Equals(text, StringComparison.OrdinalIgnoreCase))) is not null, UiTimeout);

        Assert.IsTrue(found, $"\"{text}\" isn't on screen");
        Shell($"input tap {node!.X} {node.Y}");
    }

    /// <summary>Taps the text if it's on screen right now.</summary>
    private bool TryTap(string text)
    {
        if (Dump().FirstOrDefault(n => n.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) is not { } node)
            return false;

        Shell($"input tap {node.X} {node.Y}");
        return true;
    }

    private void TapScrolling(string text)
    {
        for (var i = 0; i < 10; i++)
        {
            if (Dump().FirstOrDefault(n => n.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } node)
            {
                Shell($"input tap {node.X} {node.Y}");
                return;
            }

            Shell("input swipe 540 1700 540 800 300");
            Thread.Sleep(700);
        }

        Assert.Fail($"\"{text}\" not found after scrolling");
    }

    // ── adb ─────────────────────────────────────────────────────────────

    public string Shell(string command) => RunAdb(Adb, "-s", Serial, "shell", command);

    public static string RunAdb(string adb, params string[] args)
    {
        var info = new ProcessStartInfo(adb, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // A stuck adb (pairing, a wedged transport) must fail the test, not hang the run
        if (!process.WaitForExit(AdbCommandTimeout))
        {
            process.Kill(true);
            Assert.Fail($"adb {string.Join(' ', args)} didn't finish within {AdbCommandTimeout.TotalSeconds} s");
        }

        stderr.Wait();
        return stdout.Result;
    }

    private static string? FindAvd()
    {
        var avdDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");
        if (!Directory.Exists(avdDir))
            return null;

        foreach (var ini in Directory.GetFiles(avdDir, "*.ini"))
        {
            var target = File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("target=android-", StringComparison.Ordinal));
            if (target is not null && double.TryParse(target["target=android-".Length..], System.Globalization.CultureInfo.InvariantCulture, out var api) && api >= 30)
                return Path.GetFileNameWithoutExtension(ini);
        }

        return null;
    }

    public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(300);
        }

        return condition();
    }

    [GeneratedRegex(@"guid=[^\]\s]+")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}:(?<port>\d+)$")]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")]
    private static partial Regex BoundsRegex();
}
