using ADB_Explorer.Models;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Test;

/// <summary>
/// Live push/pull throughput benchmarks against a single connected device.
/// Default payload files are at least <see cref="MinBytesPerFile"/> (4 MiB) each;
/// the 50k × 80 KiB scenario is an explicit small-file exception.
/// Run push: dotnet test --filter Push_ParallelTransferSpeed_Sweep
/// Run pull: dotnet test --filter Pull_ParallelTransferSpeed_Sweep
/// Run 50k pull: dotnet test --filter Pull_ParallelTransferSpeed_50k
/// </summary>
[TestClass]
[DoNotParallelize]
public class TransferSpeedTests
{
    private const long TotalBytes = 1024L * 1024 * 1024;
    private const long MinBytesPerFile = 4L * 1024 * 1024;
    private const long EightyKiB = 80L * 1024;
    private const string DeviceRoot = $"{AdbExplorerConst.TEMP_PATH}/adb-explorer-speed-test";
    private const int WriteBufferSize = 1024 * 1024;
    private const string SyncV2Feature = "sendrecv_v2";

    /// <summary>
    /// 1 GiB total; counts kept so each file is at least 4 MiB (max 256 files).
    /// </summary>
    private static readonly int[] OneGiBFileCounts = [1, 10, 100, 256];

    /// <summary>
    /// Fixed 4 MiB files — closer to camera JPEGs / issue #331 small-file pulls.
    /// </summary>
    private static readonly int[] Fixed4MiBFileCounts = [100, 1000];

    private static readonly int[] ParallelismDegrees = [-1, 16, 8, 4, 1];

    private static readonly BenchmarkScenario Scenario50k80KiB =
        new(50_000, FixedBytesPerFile: EightyKiB, AllowBelowMinFileSize: true);

    private static readonly BenchmarkScenario[] OneGiBSweep =
        [.. OneGiBFileCounts.Select(c => new BenchmarkScenario(c, TotalBytes: TotalBytes))];

    private static readonly BenchmarkScenario[] Fixed4MiBSweep =
        [.. Fixed4MiBFileCounts.Select(c => new BenchmarkScenario(c, FixedBytesPerFile: MinBytesPerFile))];

    private static string? _deviceId;
    private static DeviceData? _deviceData;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _deviceId = ResolveSingleDeviceId();
        if (_deviceId is null)
            return;

        _deviceData = new AdbClient().GetDevices()
            .FirstOrDefault(d => d.Serial == _deviceId && d.State == DeviceState.Online);
    }

    [TestMethod]
    [Timeout(3_600_000)]
    public void Push_ParallelTransferSpeed_Sweep()
        => RunBenchmarks(
            TransferDirection.Push,
            [.. OneGiBSweep, .. Fixed4MiBSweep],
            "push: 1 GiB (≥4 MiB/file) + fixed 4 MiB × {100,1000}",
            maxDegreeOfParallelism: -1);

    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_ParallelTransferSpeed_Sweep()
        => RunBenchmarks(
            TransferDirection.Pull,
            [.. OneGiBSweep, .. Fixed4MiBSweep],
            "pull: 1 GiB (≥4 MiB/file) + fixed 4 MiB × {100,1000}",
            maxDegreeOfParallelism: -1);

    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_ParallelTransferSpeed_50k()
        => RunBenchmarks(
            TransferDirection.Pull,
            [Scenario50k80KiB],
            "pull: 50k × 80 KiB",
            maxDegreeOfParallelism: -1);

    [TestMethod]
    [Timeout(3_600_000)]
    public void Push_ParallelTransferSpeed_50k()
        => RunBenchmarks(
            TransferDirection.Push,
            [Scenario50k80KiB],
            "push: 50k × 80 KiB",
            maxDegreeOfParallelism: -1);

    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_ParallelismDegree_Sweep()
    {
        // Issue #331 shape: many ~4 MiB files; vary MaxDegreeOfParallelism.
        var scenarios = ParallelismDegrees
            .Select(dop => new BenchmarkScenario(1000, FixedBytesPerFile: MinBytesPerFile, MaxDegreeOfParallelism: dop))
            .ToArray();

        RunBenchmarks(
            TransferDirection.Pull,
            scenarios,
            "pull: 1000 × 4 MiB, MaxDegreeOfParallelism sweep",
            maxDegreeOfParallelism: null);
    }

    /// <summary>
    /// Isolates post-SyncService culprits for issue #331: progress-callback path
    /// (global mutex + O(n) aggregate like <c>FileSyncOperation</c>) vs classic
    /// <c>adb pull</c> (0.9-style, server-mediated, serial).
    /// Seeds once, then re-pulls the same remote payload under each mode.
    /// </summary>
    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_CulpritIsolation_ProgressAndAdbCli()
    {
        if (_deviceId is null || _deviceData is null)
        {
            Assert.Inconclusive("Exactly one adb device in 'device' state is required.");
            return;
        }

        const int fileCount = 1000;
        var useSyncV2 = DeviceSupportsSyncV2(_deviceData);
        var scenario = new BenchmarkScenario(fileCount, FixedBytesPerFile: MinBytesPerFile);
        var fileSizes = scenario.ResolveFileSizes();
        var totalBytes = fileSizes.Sum();

        var runId = Guid.NewGuid().ToString("N");
        var localDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", runId);
        var remoteDir = $"{DeviceRoot}/{runId}";

        Directory.CreateDirectory(localDir);

        try
        {
            Log("");
            Log("=== Culprit isolation: pull progress path + classic adb ===");
            Log($"Device: {_deviceId}");
            Log($"Sync v2: {useSyncV2}");
            Log($"Payload: {fileCount} × {MinBytesPerFile / (1024 * 1024)} MiB ({totalBytes / (1024.0 * 1024):F0} MiB)");
            Log("Seed once, then timed pull under each mode (DOP=∞ unless noted).");
            Log("");

            Log($"Creating payload under {localDir} ...");
            CreatePayload(localDir, fileCount, fileSizes);
            EnsureDeviceDir(_deviceId, remoteDir);

            var pushPairs = Enumerable.Range(0, fileCount)
                .Select(i => (
                    Local: Path.Combine(localDir, FileName(i)),
                    Remote: $"{remoteDir}/{FileName(i)}"))
                .ToList();

            Log("Seeding device (push, not timed) ...");
            var seed = TransferParallel(
                TransferDirection.Push,
                _deviceData,
                useSyncV2,
                pushPairs,
                fileCount,
                fileSizes[0],
                maxDegreeOfParallelism: -1,
                ProgressMode.None);

            Assert.AreEqual(0, seed.FilesFailed, $"Seed push failed: {string.Join("; ", seed.Errors.Take(3))}");
            CleanupLocal(localDir);

            Log("");
            Log($"{"Mode",-28}  {"Duration",10}  {"MB/s",8}  {"OK",6}  {"Fail",6}  {"Callbacks",10}");
            Log(new string('-', 78));

            var modes = new (string Label, ProgressMode Mode, bool ClassicAdb)[]
            {
                ("SyncService null progress", ProgressMode.None, false),
                ("+ mutex only", ProgressMode.MutexOnly, false),
                ("+ app-like aggregate O(n)", ProgressMode.AppLikeAggregate, false),
                ("classic adb pull (serial)", ProgressMode.None, true),
            };

            var results = new List<(string Label, ScenarioResult Result)>();

            foreach (var (label, mode, classicAdb) in modes)
            {
                var pullDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", $"{runId}-{SanitizeLabel(label)}");
                CleanupLocal(pullDir);
                Directory.CreateDirectory(pullDir);

                ScenarioResult result;
                if (classicAdb)
                {
                    Log($"Pulling via adb CLI → {pullDir} ...");
                    result = PullViaAdbCli(_deviceId, remoteDir, pullDir, fileCount, fileSizes[0], totalBytes);
                }
                else
                {
                    var pullPairs = Enumerable.Range(0, fileCount)
                        .Select(i => (
                            Local: Path.Combine(pullDir, FileName(i)),
                            Remote: $"{remoteDir}/{FileName(i)}"))
                        .ToList();

                    Log($"Pulling via SyncService ({label}) ...");
                    result = TransferParallel(
                        TransferDirection.Pull,
                        _deviceData,
                        useSyncV2,
                        pullPairs,
                        fileCount,
                        fileSizes[0],
                        maxDegreeOfParallelism: -1,
                        mode);
                }

                results.Add((label, result));
                Log($"{label,-28}  {result.Duration,10:mm\\:ss\\.ff}  {result.MegabytesPerSecond,8:F1}  {result.FilesSucceeded,6}  {result.FilesFailed,6}  {result.ProgressCallbacks,10}");

                if (result.FilesFailed > 0)
                    Log($"  Errors: {string.Join("; ", result.Errors.Take(3))}");

                CleanupLocal(pullDir);
            }

            Log("");
            Log("=== Summary ===");
            var baseline = results.First(r => r.Label.Contains("null progress")).Result;
            foreach (var (label, result) in results.Skip(1))
            {
                if (baseline.MegabytesPerSecond <= 0 || result.MegabytesPerSecond <= 0)
                    continue;

                var ratio = result.MegabytesPerSecond / baseline.MegabytesPerSecond;
                Log($"{label}: {result.MegabytesPerSecond:F1} MB/s ({ratio:P0} of baseline {baseline.MegabytesPerSecond:F1})");
            }

            Assert.IsTrue(results.Any(r => r.Result.FilesSucceeded > 0));
        }
        finally
        {
            CleanupLocal(localDir);
            CleanupDevice(_deviceId, remoteDir);
        }
    }

    /// <summary>
    /// Simulates Ctrl+A pull (one SyncService task per file, like N <c>FileSyncOperation</c>s)
    /// vs folder pull (one Parallel.ForEach over all files).
    /// </summary>
    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_CulpritIsolation_ManyOpsVsOneBatch()
    {
        if (_deviceId is null || _deviceData is null)
        {
            Assert.Inconclusive("Exactly one adb device in 'device' state is required.");
            return;
        }

        const int fileCount = 1000;
        var useSyncV2 = DeviceSupportsSyncV2(_deviceData);
        var fileSizes = Enumerable.Repeat(MinBytesPerFile, fileCount).ToArray();
        var totalBytes = fileSizes.Sum();

        var runId = Guid.NewGuid().ToString("N");
        var localDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", runId);
        var remoteDir = $"{DeviceRoot}/{runId}";

        Directory.CreateDirectory(localDir);

        try
        {
            Log("");
            Log("=== Culprit isolation: many ops (Ctrl+A) vs one batch (folder) ===");
            Log($"Device: {_deviceId}");
            Log($"Payload: {fileCount} × 4 MiB ({totalBytes / (1024.0 * 1024):F0} MiB)");
            Log("");

            CreatePayload(localDir, fileCount, fileSizes);
            EnsureDeviceDir(_deviceId, remoteDir);

            var pushPairs = Enumerable.Range(0, fileCount)
                .Select(i => (
                    Local: Path.Combine(localDir, FileName(i)),
                    Remote: $"{remoteDir}/{FileName(i)}"))
                .ToList();

            Log("Seeding device (push, not timed) ...");
            var seed = TransferParallel(
                TransferDirection.Push,
                _deviceData,
                useSyncV2,
                pushPairs,
                fileCount,
                fileSizes[0],
                maxDegreeOfParallelism: -1,
                ProgressMode.None);
            Assert.AreEqual(0, seed.FilesFailed);
            CleanupLocal(localDir);

            Log($"{"Mode",-36}  {"Duration",10}  {"MB/s",8}  {"OK",6}  {"Fail",6}");
            Log(new string('-', 72));

            // Folder-style: one Parallel.ForEach
            {
                var pullDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", $"{runId}-batch");
                CleanupLocal(pullDir);
                Directory.CreateDirectory(pullDir);
                var pullPairs = Enumerable.Range(0, fileCount)
                    .Select(i => (Path.Combine(pullDir, FileName(i)), $"{remoteDir}/{FileName(i)}"))
                    .ToList();

                Log("Pulling (one batch / folder-style) ...");
                var batch = TransferParallel(
                    TransferDirection.Pull, _deviceData, useSyncV2, pullPairs,
                    fileCount, fileSizes[0], -1, ProgressMode.None);
                Log($"{"one batch (folder pull)",-36}  {batch.Duration,10:mm\\:ss\\.ff}  {batch.MegabytesPerSecond,8:F1}  {batch.FilesSucceeded,6}  {batch.FilesFailed,6}");
                CleanupLocal(pullDir);
            }

            // Ctrl+A style: one Task per file (each like its own FileSyncOperation.Start)
            {
                var pullDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", $"{runId}-manyops");
                CleanupLocal(pullDir);
                Directory.CreateDirectory(pullDir);

                Log("Pulling (many ops / Ctrl+A-style) ...");
                var errors = new List<string>();
                var succeeded = 0;
                var failed = 0;
                long bytes = 0;
                var sw = Stopwatch.StartNew();

                var tasks = Enumerable.Range(0, fileCount).Select(i => Task.Run(() =>
                {
                    try
                    {
                        using var service = new SyncService(_deviceData);
                        var local = Path.Combine(pullDir, FileName(i));
                        var remote = $"{remoteDir}/{FileName(i)}";
                        using var stream = new FileStream(local, FileMode.Create, FileAccess.Write, FileShare.Read);
                        var canceled = false;
                        service.Pull(remote, stream, null, useSyncV2, in canceled);
                        Interlocked.Increment(ref succeeded);
                        Interlocked.Add(ref bytes, stream.Length);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        lock (errors)
                        {
                            if (errors.Count < 20)
                                errors.Add(ex.Message);
                        }
                    }
                })).ToArray();

                Task.WaitAll(tasks);
                sw.Stop();

                var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
                Log($"{"many ops (Ctrl+A / N FileSyncOps)",-36}  {sw.Elapsed,10:mm\\:ss\\.ff}  {bytes / 1_000_000.0 / seconds,8:F1}  {succeeded,6}  {failed,6}");
                if (failed > 0)
                    Log($"  Errors: {string.Join("; ", errors.Take(3))}");

                CleanupLocal(pullDir);
                Assert.IsTrue(succeeded > 0);
            }
        }
        finally
        {
            CleanupLocal(localDir);
            CleanupDevice(_deviceId, remoteDir);
        }
    }

    /// <summary>
    /// Same progress-path isolation at 50k × 80 KiB — O(n) aggregate cost scales with file count.
    /// </summary>
    [TestMethod]
    [Timeout(3_600_000)]
    public void Pull_CulpritIsolation_Progress_50k()
    {
        if (_deviceId is null || _deviceData is null)
        {
            Assert.Inconclusive("Exactly one adb device in 'device' state is required.");
            return;
        }

        const int fileCount = 50_000;
        var useSyncV2 = DeviceSupportsSyncV2(_deviceData);
        var fileSizes = Scenario50k80KiB.ResolveFileSizes();
        var totalBytes = fileSizes.Sum();

        var runId = Guid.NewGuid().ToString("N");
        var localDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", runId);
        var remoteDir = $"{DeviceRoot}/{runId}";

        Directory.CreateDirectory(localDir);

        try
        {
            Log("");
            Log("=== Culprit isolation: progress path at 50k × 80 KiB ===");
            Log($"Device: {_deviceId}");
            Log($"Payload: {fileCount} × 80 KiB ({totalBytes / (1024.0 * 1024):F0} MiB)");
            Log("");

            CreatePayload(localDir, fileCount, fileSizes);
            EnsureDeviceDir(_deviceId, remoteDir);

            var pushPairs = Enumerable.Range(0, fileCount)
                .Select(i => (
                    Local: Path.Combine(localDir, FileName(i)),
                    Remote: $"{remoteDir}/{FileName(i)}"))
                .ToList();

            Log("Seeding device (push, not timed) ...");
            var seed = TransferParallel(
                TransferDirection.Push,
                _deviceData,
                useSyncV2,
                pushPairs,
                fileCount,
                fileSizes[0],
                maxDegreeOfParallelism: -1,
                ProgressMode.None);
            Assert.AreEqual(0, seed.FilesFailed, $"Seed failed: {string.Join("; ", seed.Errors.Take(3))}");
            CleanupLocal(localDir);

            Log($"{"Mode",-28}  {"Duration",10}  {"MB/s",8}  {"OK",6}  {"Fail",6}  {"Callbacks",10}");
            Log(new string('-', 78));

            ScenarioResult? baseline = null;
            foreach (var (label, mode) in new (string, ProgressMode)[]
                     {
                         ("SyncService null progress", ProgressMode.None),
                         ("+ app-like aggregate O(n)", ProgressMode.AppLikeAggregate),
                     })
            {
                var pullDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", $"{runId}-{SanitizeLabel(label)}");
                CleanupLocal(pullDir);
                Directory.CreateDirectory(pullDir);

                var pullPairs = Enumerable.Range(0, fileCount)
                    .Select(i => (
                        Local: Path.Combine(pullDir, FileName(i)),
                        Remote: $"{remoteDir}/{FileName(i)}"))
                    .ToList();

                Log($"Pulling ({label}) ...");
                var result = TransferParallel(
                    TransferDirection.Pull,
                    _deviceData,
                    useSyncV2,
                    pullPairs,
                    fileCount,
                    fileSizes[0],
                    maxDegreeOfParallelism: -1,
                    mode);

                baseline ??= result;
                var vsBase = baseline.MegabytesPerSecond > 0
                    ? $" ({result.MegabytesPerSecond / baseline.MegabytesPerSecond:P0} of baseline)"
                    : "";

                Log($"{label,-28}  {result.Duration,10:mm\\:ss\\.ff}  {result.MegabytesPerSecond,8:F1}  {result.FilesSucceeded,6}  {result.FilesFailed,6}  {result.ProgressCallbacks,10}{vsBase}");
                CleanupLocal(pullDir);

                Assert.AreEqual(0, result.FilesFailed, $"{label} had failures");
            }
        }
        finally
        {
            CleanupLocal(localDir);
            CleanupDevice(_deviceId, remoteDir);
        }
    }

    private void RunBenchmarks(
        TransferDirection direction,
        BenchmarkScenario[] scenarios,
        string suiteLabel,
        int? maxDegreeOfParallelism)
    {
        if (_deviceId is null || _deviceData is null)
        {
            Assert.Inconclusive("Exactly one adb device in 'device' state is required.");
            return;
        }

        var useSyncV2 = DeviceSupportsSyncV2(_deviceData);

        Log("");
        Log($"=== ADB Explorer parallel {direction.ToString().ToLowerInvariant()} speed test ===");
        Log($"Suite: {suiteLabel}");
        Log($"Device: {_deviceId}");
        Log($"Sync v2: {useSyncV2}");
        Log($"Min file size: {MinBytesPerFile / (1024 * 1024)} MiB");
        Log("");
        Log($"{"Files",8}  {"Per file",10}  {"Total",10}  {"DOP",5}  {"Duration",10}  {"MB/s",8}  {"OK",6}  {"Fail",6}");
        Log(new string('-', 80));

        var results = new List<ScenarioResult>();

        foreach (var scenario in scenarios)
        {
            var dop = scenario.MaxDegreeOfParallelism ?? maxDegreeOfParallelism ?? -1;
            var result = RunScenario(_deviceId, _deviceData, scenario, direction, useSyncV2, dop, Log);
            results.Add(result);

            Log($"{result.FileCount,8}  {result.BytesPerFile,10}  {result.BytesTransferred,10}  {FormatDop(result.MaxDegreeOfParallelism),5}  {result.Duration,10:mm\\:ss\\.ff}  {result.MegabytesPerSecond,8:F1}  {result.FilesSucceeded,6}  {result.FilesFailed,6}");

            if (result.FilesFailed > 0)
                Log($"  Errors: {string.Join("; ", result.Errors.Take(3))}");
        }

        Log("");
        Log("=== Summary ===");
        var best = results.Where(r => r.FilesFailed == 0).MaxBy(r => r.MegabytesPerSecond);
        if (best is not null)
            Log($"Peak throughput: {best.MegabytesPerSecond:F1} MB/s at {best.FileCount} files (DOP={FormatDop(best.MaxDegreeOfParallelism)})");

        var firstChoke = FindFirstChokePoint(results);
        if (firstChoke is not null)
            Log($"First choke signal: {firstChoke.FileCount} files / DOP={FormatDop(firstChoke.MaxDegreeOfParallelism)} (throughput drop or failures vs prior scenario)");
        else
            Log("No clear choke point detected in this sweep.");

        Assert.IsTrue(results.Any(r => r.FilesSucceeded > 0), "No files transferred successfully.");
    }

    private static ScenarioResult RunScenario(
        string deviceId,
        DeviceData deviceData,
        BenchmarkScenario scenario,
        TransferDirection direction,
        bool useSyncV2,
        int maxDegreeOfParallelism,
        Action<string> log)
    {
        var fileCount = scenario.FileCount;
        var runId = Guid.NewGuid().ToString("N");
        var localDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", runId);
        var pullDir = Path.Combine(Path.GetTempPath(), "AdbExplorerSpeedTest", $"{runId}-pull");
        var remoteDir = $"{DeviceRoot}/{runId}";
        var fileSizes = scenario.ResolveFileSizes();
        var totalBytes = fileSizes.Sum();

        Directory.CreateDirectory(localDir);

        try
        {
            log($"Creating {fileCount} payload files ({totalBytes / (1024.0 * 1024):F0} MiB total) under {localDir} ...");
            var createSw = Stopwatch.StartNew();
            CreatePayload(localDir, fileCount, fileSizes);
            createSw.Stop();
            log($"  Payload ready in {createSw.Elapsed:mm\\:ss\\.ff}");

            EnsureDeviceDir(deviceId, remoteDir);

            var pushPairs = Enumerable.Range(0, fileCount)
                .Select(i => (
                    Local: Path.Combine(localDir, FileName(i)),
                    Remote: $"{remoteDir}/{FileName(i)}"))
                .ToList();

            if (direction is TransferDirection.Push)
            {
                log($"Pushing {fileCount} files to {remoteDir} (DOP={FormatDop(maxDegreeOfParallelism)}) ...");
                return TransferParallel(
                    TransferDirection.Push,
                    deviceData,
                    useSyncV2,
                    pushPairs,
                    fileCount,
                    fileSizes[0],
                    maxDegreeOfParallelism);
            }

            // Seed device for pull (not counted in pull throughput).
            log($"Seeding device (push {fileCount} files, not timed) ...");
            var seed = TransferParallel(
                TransferDirection.Push,
                deviceData,
                useSyncV2,
                pushPairs,
                fileCount,
                fileSizes[0],
                maxDegreeOfParallelism: -1,
                ProgressMode.None);

            if (seed.FilesFailed > 0)
            {
                log($"  Seed push had {seed.FilesFailed} failures — aborting pull scenario.");
                return new ScenarioResult(
                    fileCount,
                    fileSizes[0],
                    TimeSpan.Zero,
                    0,
                    0,
                    fileCount,
                    0,
                    maxDegreeOfParallelism,
                    seed.Errors);
            }

            CleanupLocal(localDir);
            Directory.CreateDirectory(pullDir);

            var pullPairs = Enumerable.Range(0, fileCount)
                .Select(i => (
                    Local: Path.Combine(pullDir, FileName(i)),
                    Remote: $"{remoteDir}/{FileName(i)}"))
                .ToList();

            log($"Pulling {fileCount} files to {pullDir} (DOP={FormatDop(maxDegreeOfParallelism)}) ...");
            return TransferParallel(
                TransferDirection.Pull,
                deviceData,
                useSyncV2,
                pullPairs,
                fileCount,
                fileSizes[0],
                maxDegreeOfParallelism,
                ProgressMode.None);
        }
        finally
        {
            CleanupLocal(localDir);
            CleanupLocal(pullDir);
            CleanupDevice(deviceId, remoteDir);
        }
    }

    private static ScenarioResult TransferParallel(
        TransferDirection direction,
        DeviceData deviceData,
        bool useSyncV2,
        List<(string Local, string Remote)> files,
        int fileCount,
        long nominalBytesPerFile,
        int maxDegreeOfParallelism,
        ProgressMode progressMode = ProgressMode.None)
    {
        var fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;
        var errors = new List<string>();
        var succeeded = 0;
        var failed = 0;
        long bytesTransferred = 0;
        long progressCallbacks = 0;

        // Mirrors FileSyncOperation progress bookkeeping.
        var lastReportedBytes = new ConcurrentDictionary<string, long>();
        var lastRawReceivedBytes = new ConcurrentDictionary<string, ulong>();
        var receivedBytesCarry = new ConcurrentDictionary<string, long>();
        var perFileBytes = new long[fileCount];
        var perFileSize = Enumerable.Repeat(nominalBytesPerFile, fileCount).ToArray();
        var progressList = new List<object>(progressMode is ProgressMode.AppLikeAggregate ? 1024 : 0);
        using var mutex = new Mutex();

        long totalBytes = nominalBytesPerFile * fileCount;

        var pathIndex = files
            .Select((f, i) => (f.Remote, i))
            .ToDictionary(x => x.Remote, x => x.i, StringComparer.Ordinal);

        var sw = Stopwatch.StartNew();

        Parallel.ForEach(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            file =>
            {
                try
                {
                    using var service = new SyncService(deviceData);
                    var canceled = false;
                    var index = pathIndex[file.Remote];

                    void Callback(SyncProgressChangedEventArgs e)
                    {
                        Interlocked.Increment(ref progressCallbacks);
                        if (progressMode is ProgressMode.None)
                            return;

                        var currentBytes = CorrectReceivedBytes(
                            file.Remote,
                            e.ReceivedBytesSize,
                            lastRawReceivedBytes,
                            receivedBytesCarry);

                        if (progressMode is ProgressMode.MutexOnly)
                        {
                            mutex.WaitOne();
                            lastReportedBytes[file.Remote] = currentBytes;
                            mutex.ReleaseMutex();
                            return;
                        }

                        // AppLikeAggregate — same shape as FileSyncOperation.AddUpdates
                        // + ProgressUpdates_CollectionChanged (global mutex, then O(n) sums).
                        mutex.WaitOne();
                        try
                        {
                            lastReportedBytes[file.Remote] = currentBytes;
                            perFileBytes[index] = currentBytes;

                            double? filePct = perFileSize[index] > 0
                                ? Math.Min(100, currentBytes * 100.0 / perFileSize[index])
                                : 100.0;

                            progressList.Add((file.Remote, filePct, currentBytes));

                            // CollectionChanged work: Files.Sum / ActiveFiles / Average
                            long sum = 0;
                            var active = 0;
                            double pctSum = 0;
                            for (var i = 0; i < fileCount; i++)
                            {
                                var b = perFileBytes[i];
                                sum += b;
                                if (perFileSize[i] <= 0)
                                    continue;

                                var pct = b * 100.0 / perFileSize[i];
                                if (pct is > 0 and < 100)
                                {
                                    active++;
                                    pctSum += pct;
                                }
                            }

                            _ = totalBytes > 0 ? sum * 100.0 / totalBytes : 0;
                            _ = active > 0 ? pctSum / active : 0;
                            _ = progressList.Count;
                        }
                        finally
                        {
                            mutex.ReleaseMutex();
                        }
                    }

                    Action<SyncProgressChangedEventArgs>? callback =
                        progressMode is ProgressMode.None ? null : Callback;

                    if (direction is TransferDirection.Push)
                    {
                        using var stream = new FileStream(file.Local, FileMode.Open, FileAccess.Read, FileShare.Read);
                        service.Push(stream, file.Remote, fileMode, DateTime.Now, callback, useSyncV2, in canceled);
                        Interlocked.Add(ref bytesTransferred, stream.Length);
                    }
                    else
                    {
                        using var stream = new FileStream(file.Local, FileMode.Create, FileAccess.Write, FileShare.Read);
                        service.Pull(file.Remote, stream, callback, useSyncV2, in canceled);
                        Interlocked.Add(ref bytesTransferred, stream.Length);
                    }

                    Interlocked.Increment(ref succeeded);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    lock (errors)
                    {
                        if (errors.Count < 20)
                            errors.Add($"{Path.GetFileName(file.Local)}: {ex.Message}");
                    }
                }
            });

        sw.Stop();

        var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        var mbPerSec = bytesTransferred / 1_000_000.0 / seconds;

        return new ScenarioResult(
            fileCount,
            nominalBytesPerFile,
            sw.Elapsed,
            mbPerSec,
            succeeded,
            failed,
            bytesTransferred,
            maxDegreeOfParallelism,
            errors,
            progressCallbacks);
    }

    private static ScenarioResult PullViaAdbCli(
        string deviceId,
        string remoteDir,
        string localDir,
        int fileCount,
        long bytesPerFile,
        long expectedTotalBytes)
    {
        var sw = Stopwatch.StartNew();
        var result = RunAdb(["-s", deviceId, "pull", remoteDir, localDir]);
        sw.Stop();

        var errors = new List<string>();
        if (result.ExitCode != 0)
            errors.Add(result.Stderr.Trim());

        // adb pull nests under localDir/<remote-basename>/
        var pulledRoot = Directory.Exists(Path.Combine(localDir, Path.GetFileName(remoteDir)))
            ? Path.Combine(localDir, Path.GetFileName(remoteDir))
            : localDir;

        var pulled = Directory.Exists(pulledRoot)
            ? Directory.GetFiles(pulledRoot, "bench_*.bin", SearchOption.AllDirectories)
            : [];

        long bytes = 0;
        foreach (var f in pulled)
            bytes += new FileInfo(f).Length;

        var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        var ok = pulled.Length;
        var fail = result.ExitCode == 0 ? Math.Max(0, fileCount - ok) : fileCount;

        return new ScenarioResult(
            fileCount,
            bytesPerFile,
            sw.Elapsed,
            bytes / 1_000_000.0 / seconds,
            ok,
            fail,
            bytes > 0 ? bytes : expectedTotalBytes,
            MaxDegreeOfParallelism: 1,
            errors,
            ProgressCallbacks: 0);
    }

    private static long CorrectReceivedBytes(
        string path,
        ulong rawReceived,
        ConcurrentDictionary<string, ulong> lastRawReceivedBytes,
        ConcurrentDictionary<string, long> receivedBytesCarry)
    {
        var lastRaw = lastRawReceivedBytes.GetOrAdd(path, 0);
        var carry = receivedBytesCarry.GetOrAdd(path, 0L);

        if (rawReceived < lastRaw)
            receivedBytesCarry[path] = carry += 1L << 32;

        lastRawReceivedBytes[path] = rawReceived;
        return carry + (long)rawReceived;
    }

    private static string SanitizeLabel(string label)
        => string.Concat(label.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')).ToLowerInvariant();

    private static void CreatePayload(string localDir, int fileCount, long[] fileSizes)
    {
        var buffer = new byte[WriteBufferSize];
        Random.Shared.NextBytes(buffer);

        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(localDir, FileName(i));
            var remaining = fileSizes[i];

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);

            while (remaining > 0)
            {
                var chunk = (int)Math.Min(buffer.Length, remaining);
                stream.Write(buffer, 0, chunk);
                remaining -= chunk;
            }

            if (i % Math.Max(1, fileCount / 10) == 0 || i == fileCount - 1)
                Console.WriteLine($"  Created {i + 1}/{fileCount} files");
        }
    }

    private static long[] DistributeBytes(long totalBytes, int fileCount)
    {
        var baseSize = totalBytes / fileCount;
        if (baseSize < MinBytesPerFile)
        {
            throw new InvalidOperationException(
                $"Per-file size {baseSize} for {fileCount} files totaling {totalBytes} is below min {MinBytesPerFile}.");
        }

        var remainder = totalBytes - baseSize * fileCount;
        var sizes = new long[fileCount];

        for (var i = 0; i < fileCount; i++)
            sizes[i] = baseSize + (i < remainder ? 1 : 0);

        return sizes;
    }

    private static string FileName(int index) => $"bench_{index:D5}.bin";

    private static string FormatDop(int dop) => dop < 0 ? "∞" : dop.ToString();

    private readonly record struct BenchmarkScenario(
        int FileCount,
        long? TotalBytes = null,
        long? FixedBytesPerFile = null,
        int? MaxDegreeOfParallelism = null,
        bool AllowBelowMinFileSize = false)
    {
        public long[] ResolveFileSizes()
        {
            if (FixedBytesPerFile is > 0)
            {
                if (!AllowBelowMinFileSize && FixedBytesPerFile.Value < MinBytesPerFile)
                {
                    throw new InvalidOperationException(
                        $"FixedBytesPerFile {FixedBytesPerFile} is below min {MinBytesPerFile}.");
                }

                return Enumerable.Repeat(FixedBytesPerFile.Value, FileCount).ToArray();
            }

            if (TotalBytes is > 0)
                return DistributeBytes(TotalBytes.Value, FileCount);

            throw new InvalidOperationException("Scenario must specify TotalBytes or FixedBytesPerFile.");
        }
    }

    private static void EnsureDeviceDir(string deviceId, string remoteDir)
    {
        var result = RunAdb(["-s", deviceId, "shell", "mkdir", "-p", ShellQuote(remoteDir)]);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"mkdir {remoteDir} failed: {result.Stderr.Trim()}");
    }

    private static void CleanupLocal(string localDir)
    {
        try
        {
            if (Directory.Exists(localDir))
                Directory.Delete(localDir, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Local cleanup failed for {localDir}: {ex.Message}");
        }
    }

    private static void CleanupDevice(string deviceId, string remoteDir)
    {
        RunAdb(["-s", deviceId, "shell", "rm", "-rf", ShellQuote(remoteDir)]);
    }

    private static bool DeviceSupportsSyncV2(DeviceData deviceData)
    {
        try
        {
            var features = new AdbClient().GetFeatureSet(deviceData);
            return features.Any(f => f.Contains(SyncV2Feature, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveSingleDeviceId()
    {
        var result = RunAdb(["devices"]);
        if (result.ExitCode != 0)
            return null;

        var ids = result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.EndsWith("\tdevice", StringComparison.Ordinal))
            .Select(l => l.Split('\t')[0])
            .ToList();

        return ids.Count == 1 ? ids[0] : null;
    }

    private static string ShellQuote(string str)
        => "\"" + str.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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

    private static ScenarioResult? FindFirstChokePoint(IReadOnlyList<ScenarioResult> results)
    {
        ScenarioResult? previous = null;

        foreach (var current in results)
        {
            if (previous is null)
            {
                previous = current;
                continue;
            }

            if (current.FilesFailed > 0)
                return current;

            if (previous.MegabytesPerSecond > 0
                && current.MegabytesPerSecond < previous.MegabytesPerSecond * 0.7)
                return current;

            previous = current;
        }

        return null;
    }

    private void Log(string line)
    {
        Console.WriteLine(line);
        TestContext.WriteLine(line);
    }

    private enum TransferDirection
    {
        Push,
        Pull,
    }

    private enum ProgressMode
    {
        None,
        MutexOnly,
        /// <summary>Global mutex + O(n) aggregate per callback, matching FileSyncOperation.</summary>
        AppLikeAggregate,
    }

    private sealed record ScenarioResult(
        int FileCount,
        long BytesPerFile,
        TimeSpan Duration,
        double MegabytesPerSecond,
        int FilesSucceeded,
        int FilesFailed,
        long BytesTransferred,
        int MaxDegreeOfParallelism,
        List<string> Errors,
        long ProgressCallbacks = 0);

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr, string Arguments);
}
