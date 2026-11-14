#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADB_Test;

/// <summary>
/// Directory listing and single-path stat: our adb.exe/shell route vs the sync protocol in process (LIS2/LIST, STA2),
/// on every USB device. Opt in with ADB_EXPLORER_BENCH=1. Run: dotnet test --filter SyncListing_Benchmark
/// </summary>
[TestClass]
public class SyncListingBenchmark
{
    private const string BenchRoot = "/sdcard/adb-explorer-bench-ls";
    private static readonly int[] FolderSizes = [10, 300, 3000];

    private record Row(string Device, string Op, string Method, int Runs, double MedianMs, double P90Ms);

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);
    }

    [TestMethod]
    [Timeout(300_000)]
    public void SyncListing_Benchmark()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_BENCH=1 to run the benchmark.");

        var devices = new AdbClient().GetDevices()
            .Where(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"))
            .ToList();

        if (devices.Count == 0 || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        List<Row> rows = [];
        var notes = new StringBuilder();

        foreach (var device in devices)
        {
            var features = AdbService.GetDeviceFeatures(device);
            var lsV2 = features.Contains(AdbService.FEATURE_LS_V2);
            var statV2 = features.Contains("stat_v2");
            var label = $"{device.Model} (Android {Shell(device, "getprop ro.build.version.release")})";
            notes.AppendLine($"- {label}: ls_v2={lsV2}, stat_v2={statV2}");

            try
            {
                var setup = string.Join(" && ", FolderSizes.Select(n =>
                    $"mkdir -p {BenchRoot}/d{n} && cd {BenchRoot}/d{n} && seq 1 {n} | sed 's/^/f/;s/$/.bin/' | xargs touch"));
                Shell(device, $"rm -rf {BenchRoot}; {setup}; head -c 5000000 /dev/urandom > {BenchRoot}/stat.bin");

                // A listing leaves the socket stale (the library reopens it before the next listing), so stats get their own
                using var sync = new SyncService(device);
                using var statSync = new SyncService(device);

                foreach (var n in FolderSizes)
                {
                    var path = $"{BenchRoot}/d{n}";
                    var runs = n >= 3000 ? 10 : 25;
                    var op = $"List {n}";

                    rows.Add(Measure(label, op, "adb.exe ls (current)", runs,
                        () => AdbService.ListDirectoryEntries(device.Serial, path, CancellationToken.None).Count()));

                    var syncName = lsV2 ? "LIS2" : "LIST v1";
                    rows.Add(Measure(label, op, $"{syncName}, reused service (reopens)", runs, () => SyncList(sync, path, lsV2)));
                    rows.Add(Measure(label, op, $"{syncName}, new socket", runs, () =>
                    {
                        using var fresh = new SyncService(device);
                        return SyncList(fresh, path, lsV2);
                    }));

                    CompareListings(label, device.Serial, path, sync, lsV2, notes);
                }

                var file = $"{BenchRoot}/stat.bin";
                const int statRuns = 40;

                rows.Add(Measure(label, "Stat", "shell stat -c%s (TryGetFileSize)", statRuns,
                    () => AdbService.TryGetFileSize(device.Serial, file, CancellationToken.None)));
                rows.Add(Measure(label, "Stat", "shell stat, all fields (GetFileExtraInfoAsync)", statRuns,
                    () => AdbService.GetFileExtraInfoAsync(device.Serial, file, CancellationToken.None).GetAwaiter().GetResult()));

                if (statV2)
                {
                    rows.Add(Measure(label, "Stat", "STA2, open socket", statRuns, () => statSync.StatEx(file)));
                    rows.Add(Measure(label, "Stat", "STA2, new socket", statRuns, () =>
                    {
                        using var fresh = new SyncService(device);
                        return fresh.StatEx(file);
                    }));

                    CompareStat(label, file, statSync, device, notes);
                }

                rows.Add(Measure(label, "Stat", "STAT v1, open socket", statRuns, () => statSync.Stat(file)));
            }
            finally
            {
                Shell(device, $"rm -rf {BenchRoot}");
            }
        }

        var report = new StringBuilder("| Device | Op | Method | Runs | Median ms | P90 ms |\n|---|---|---|---|---|---|\n");
        foreach (var r in rows)
            report.AppendLine($"| {r.Device} | {r.Op} | {r.Method} | {r.Runs} | {r.MedianMs:F1} | {r.P90Ms:F1} |");

        report.AppendLine().AppendLine(notes.ToString());

        var reportPath = Path.Combine(Path.GetTempPath(), "adb-explorer-listing-bench.md");
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(report);
        Console.WriteLine(reportPath);
    }

    /// <summary>
    /// Folder tree building: GetFolderTree's find (with -printf, or the while read; stat loop where toybox lacks it)
    /// vs a per-folder walk, over adb.exe ls and over the sync protocol in process. Run: dotnet test --filter SyncTree_Benchmark
    /// </summary>
    [TestMethod]
    [Timeout(600_000)]
    public void SyncTree_Benchmark()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_BENCH=1 to run the benchmark.");

        var devices = new AdbClient().GetDevices()
            .Where(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"))
            .ToList();

        if (devices.Count == 0 || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        (string Name, int Dirs, int FilesPerDir, int Runs)[] trees = [("5 folders × 10 files", 5, 10, 5), ("40 folders × 25 files", 40, 25, 3)];
        List<Row> rows = [];
        var notes = new StringBuilder();

        foreach (var device in devices)
        {
            ShellCommands.FindCommands(device.Serial);
            var lsV2 = AdbService.GetDeviceFeatures(device).Contains(AdbService.FEATURE_LS_V2);
            var label = $"{device.Model} (Android {Shell(device, "getprop ro.build.version.release")})";
            var findName = ShellCommands.FindPrintf(device.Serial) ? "find -printf" : "find | while read; stat";

            try
            {
                foreach (var tree in trees)
                {
                    var root = $"{BenchRoot}/tree";
                    Shell(device, $"rm -rf {BenchRoot}; mkdir -p {root} && cd {root} && for d in $(seq 1 {tree.Dirs}); do mkdir -p d$d/sub "
                        + $"&& seq 1 {tree.FilesPerDir} | sed \"s|^|d$d/f|;s|$|.bin|\" | xargs touch; done");

                    var findCount = FileHelper.GetFolderTree([root], deviceId: device.Serial).Length;
                    var walkCount = SyncWalk(device, root, lsV2);
                    notes.AppendLine($"- {label} {tree.Name}: {findName} found {findCount} entries, sync walk {walkCount}");

                    rows.Add(Measure(label, tree.Name, $"GetFolderTree ({findName})", tree.Runs,
                        () => FileHelper.GetFolderTree([root], deviceId: device.Serial).Length));
                    rows.Add(Measure(label, tree.Name, "adb.exe ls walk", tree.Runs,
                        () => AdbService.ListDirectoryRecursive(device.Serial, root, CancellationToken.None).Count()));
                    rows.Add(Measure(label, tree.Name, $"{(lsV2 ? "LIS2" : "LIST v1")} walk, in process", tree.Runs,
                        () => SyncWalk(device, root, lsV2)));
                }
            }
            finally
            {
                Shell(device, $"rm -rf {BenchRoot}");
            }
        }

        var report = new StringBuilder("| Device | Tree | Method | Runs | Median ms | P90 ms |\n|---|---|---|---|---|---|\n");
        foreach (var r in rows)
            report.AppendLine($"| {r.Device} | {r.Op} | {r.Method} | {r.Runs} | {r.MedianMs:F1} | {r.P90Ms:F1} |");

        report.AppendLine().AppendLine(notes.ToString());

        var reportPath = Path.Combine(Path.GetTempPath(), "adb-explorer-tree-bench.md");
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(report);
        Console.WriteLine(reportPath);
    }

    private static int SyncWalk(DeviceData device, string root, bool lsV2)
    {
        using var sync = new SyncService(device);
        var queue = new Queue<string>([root]);
        var count = 0;

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<IFileStatistics> entries = lsV2
                ? sync.GetDirectoryListingEx(dir)
                : sync.GetDirectoryListing(dir);

            foreach (var entry in entries.Where(e => e.Path is not "." and not ".."))
            {
                count++;
                if (((int)entry.FileMode & 0xF000) == 0x4000)
                    queue.Enqueue($"{dir}/{entry.Path}");
            }
        }

        return count;
    }

    private static int SyncList(SyncService sync, string path, bool lsV2)
    {
        if (lsV2)
            return sync.GetDirectoryListingEx(path).Count();

        return sync.GetDirectoryListing(path).Count();
    }

    private static Row Measure(string device, string op, string method, int runs, Func<object?> action)
    {
        action();

        var times = new double[runs];
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);

        return new(device, op, method, runs, times[runs / 2], times[(int)Math.Ceiling(runs * 0.9) - 1]);
    }

    private static void CompareListings(string device, string serial, string path, SyncService sync, bool lsV2, StringBuilder notes)
    {
        var current = AdbService.ListDirectoryEntries(serial, path, CancellationToken.None)
            .ToDictionary(f => f.FullName);

        IEnumerable<IFileStatistics> synced = lsV2
            ? sync.GetDirectoryListingEx(path)
            : sync.GetDirectoryListing(path);

        var mismatches = 0;
        var count = 0;
        foreach (var entry in synced.Where(e => e.Path is not "." and not ".."))
        {
            count++;

            if (!current.TryGetValue(entry.Path, out var stat)
                || stat.Size is long size && (ulong)size != entry.Size
                || stat.ModifiedTime != entry.Time.DateTime.ToLocalTime()
                || stat.Permissions is { } perms && (int)perms != ((int)entry.FileMode & 511))
                mismatches++;
        }

        if (count != current.Count)
            mismatches += Math.Abs(count - current.Count);

        notes.AppendLine($"- {device} {path}: {count} entries, {mismatches} mismatches vs adb.exe ls");
    }

    private static void CompareStat(string device, string file, SyncService sync, DeviceData deviceData, StringBuilder notes)
    {
        var shell = AdbService.GetFileExtraInfoAsync(deviceData.Serial, file, CancellationToken.None).GetAwaiter().GetResult();
        var sta2 = sync.StatEx(file);

        if (shell is not { } info)
        {
            notes.AppendLine($"- {device}: shell stat returned nothing");
            return;
        }

        notes.AppendLine($"- {device} stat fields, shell vs STA2:");
        notes.AppendLine($"  - size: {info.Size} vs {sta2.Size}");
        notes.AppendLine($"  - uid/gid: {info.OwnerUid}/{info.OwnerGid} vs {sta2.UserId}/{sta2.GroupId}");
        notes.AppendLine($"  - user/group names: {info.User}/{info.Group} vs (not in STA2)");
        notes.AppendLine($"  - permissions: {Convert.ToString((int)(info.Permissions ?? 0), 8)} vs {Convert.ToString((int)sta2.FileMode & 4095, 8)}");
        notes.AppendLine($"  - mtime: {info.ModifiedTime:O} vs {sta2.ModifiedTime:O}");
        notes.AppendLine($"  - atime: {info.AccessTime:O} vs {sta2.AccessTime:O}");
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
