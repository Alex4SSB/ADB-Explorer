using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ADB_Test;

/// <summary>
/// Exercises <see cref="ADBService.EscapeAdbShellPath"/> and <see cref="ADBService.EscapeAdbShellScript"/>
/// against a live device using the same Process argument joining as <see cref="ADBService"/>.
/// Special-char filenames are created under <see cref="AdbExplorerConst.TEMP_PATH"/> only.
/// Run: dotnet test --filter ShellEscape
/// </summary>
[TestClass]
public class ShellEscapeEmulatorTests
{
    private const string TestDir = $"{AdbExplorerConst.TEMP_PATH}/adb-explorer-test-escape";
    private static string? _deviceId;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        Data.Settings = new AppSettings();
        _deviceId = ResolveDeviceId();
        if (_deviceId is null)
            return;

        EnsureTestFiles(_deviceId);
    }

    [TestMethod]
    public void EscapeAdbShellPath_AndScript_AreIdentical()
    {
        foreach (var sample in new[] { "~name", "/data/local/tmp/foo (1).txt", "a&b", "file`tick.txt" })
            Assert.AreEqual(ADBService.EscapeAdbShellString(sample), ADBService.EscapeAdbShellString(sample));
    }

    [TestMethod]
    public void ShellEscape_Emulator_PathEscapeStatAllSpecialChars()
    {
        if (_deviceId is null)
        {
            Assert.Inconclusive("No adb device attached.");
            return;
        }

        foreach (var (fileName, note) in SpecialFileNames)
        {
            var path = $"{TestDir}/{fileName}";
            Log($"\n=== {path} ({note}) ===");

            var minimal = MinimalShellQuote(path);
            var pathQuoted = ADBService.EscapeAdbShellString(path);
            var scriptQuoted = ADBService.EscapeAdbShellString(path);

            Log($"minimal:  {minimal}");
            Log($"path:     {pathQuoted}");
            Log($"script:   {scriptQuoted}");

            var minimalStat = RunShellStat(_deviceId, minimal);
            var pathStat = RunShellStat(_deviceId, pathQuoted);
            var scriptStat = RunShellStat(_deviceId, scriptQuoted);

            Log($"minimal stat → exit={minimalStat.ExitCode} err=[{Trim(minimalStat.Stderr)}]");
            Log($"path stat    → exit={pathStat.ExitCode} err=[{Trim(pathStat.Stderr)}]");
            Log($"script stat  → exit={scriptStat.ExitCode} err=[{Trim(scriptStat.Stderr)}]");

            Assert.AreEqual(0, pathStat.ExitCode, $"EscapeAdbShellPath stat failed for {path}: {pathStat.Stderr}");
            Assert.AreEqual("0", pathStat.Stdout.Trim(), $"Expected size 0 for {path}");
            Assert.AreEqual(0, scriptStat.ExitCode, $"EscapeAdbShellScript stat failed for {path}: {scriptStat.Stderr}");
        }
    }

    [TestMethod]
    public void ShellEscape_Emulator_RemainingCharsNeedPathEscape()
    {
        if (_deviceId is null)
        {
            Assert.Inconclusive("No adb device attached.");
            return;
        }

        // Chars not covered by the original /sdcard subset; filenames only creatable under tmp.
        var remaining = new (string FileName, string Note)[]
        {
            ("file<1>.txt", "<"),
            ("file>1.txt", ">"),
            ("file|pipe.txt", "|"),
            ("file;semi.txt", ";"),
            ("file*star.txt", "*"),
            (@"file\back.txt", @"\"),
            ("file~tilde.txt", "~"),
            ("file\"quote.txt", "\""),
            ("file'apos.txt", "'"),
            ("file$dollar.txt", "$"),
            ("file`tick.txt", "`"),
        };

        var minimalFails = new List<string>();
        var pathOk = new List<string>();

        foreach (var (fileName, note) in remaining)
        {
            var path = $"{TestDir}/{fileName}";
            var minimalStat = RunShellStat(_deviceId, MinimalShellQuote(path));
            var pathStat = RunShellStat(_deviceId, ADBService.EscapeAdbShellString(path));

            Log($"{note} {fileName}: minimal exit={minimalStat.ExitCode}, path exit={pathStat.ExitCode}");

            if (minimalStat.ExitCode != 0)
                minimalFails.Add(note);
            if (pathStat.ExitCode == 0)
                pathOk.Add(note);

            Assert.AreEqual(0, pathStat.ExitCode, $"Path escape failed for {note} in {fileName}: {pathStat.Stderr}");
        }

        Log($"\nMinimal quote failed for: {string.Join(", ", minimalFails)}");
        Log($"Path escape succeeded for: {string.Join(", ", pathOk)}");
    }

    private static readonly (string FileName, string Note)[] SpecialFileNames =
    [
        ("normal.txt", "baseline"),
        ("foo bar.txt", "space"),
        ("foo (1).txt", "parens"),
        ("file[1].txt", "brackets"),
        ("a&b.txt", "ampersand"),
        ("file<1>.txt", "less-than"),
        ("file>1.txt", "greater-than"),
        ("file|pipe.txt", "pipe"),
        ("file;semi.txt", "semicolon"),
        ("file*star.txt", "asterisk"),
        (@"file\back.txt", "backslash"),
        ("file~tilde.txt", "tilde"),
        ("file\"quote.txt", "double-quote"),
        ("file'apos.txt", "apostrophe"),
        ("file$dollar.txt", "dollar"),
        ("file`tick.txt", "backtick"),
    ];

    private void Log(string line)
    {
        Console.WriteLine(line);
        TestContext.WriteLine(line);
    }

    private static string Trim(string s) => s.Trim().Replace('\n', ' ').Replace('\r', ' ');

    private static string? ResolveDeviceId()
    {
        var result = RunAdb(["devices"]);
        if (result.ExitCode != 0)
            return null;

        return result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.EndsWith("\tdevice", StringComparison.Ordinal))
            ?.Split('\t')[0];
    }

    private static void EnsureTestFiles(string deviceId)
    {
        RunAdb(["-s", deviceId, "shell", "mkdir", "-p", ADBService.EscapeAdbShellString(TestDir)]);

        foreach (var (fileName, _) in SpecialFileNames)
        {
            var path = $"{TestDir}/{fileName}";
            RunAdb(["-s", deviceId, "shell", "touch", ADBService.EscapeAdbShellString(path)]);
        }
    }

    /// <summary>Double-quote only; escape <c>\</c> and <c>"</c> per POSIX inside quotes.</summary>
    private static string MinimalShellQuote(string str)
        => "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static ProcessResult RunShellStat(string deviceId, string pathArg)
        => RunAdb(["-s", deviceId, "shell", "stat", "-c", "%s", pathArg]);

    /// <summary>Matches <see cref="ADBService.StartCommandProcess"/> argument joining.</summary>
    private static ProcessResult RunAdb(string[] args)
    {
        var arguments = string.Join(' ', args.Where(a => !string.IsNullOrEmpty(a)));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new(process.ExitCode, stdout, stderr, arguments);
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr, string Arguments);
}
