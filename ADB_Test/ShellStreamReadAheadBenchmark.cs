#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Test;

/// <summary>
/// The largest and smallest files of a device folder dragged to Explorer (streamed to the shell), against pulling them
/// directly, over USB and over USB + Wi-Fi. Reads existing files only; nothing on the device is created or changed.
/// Opt in with ADB_EXPLORER_BENCH=1; ADB_EXPLORER_BENCH_FOLDER, _COUNT and _SMALL_COUNT pick the files.
/// </summary>
public partial class ShellStreamTests
{
    // Set per run from its expected time: its bytes at 30 MB/s a lane, plus 20 ms a file, and 10% more
    private static TimeSpan ReadAheadRunLimit = TimeSpan.FromSeconds(34);

    [TestMethod]
    public void ShellStream_ReadAheadBenchmark()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_BENCH=1 to run the benchmark.");

        var devices = new AdbClient().GetDevices().Where(d => d.State is DeviceState.Online).ToList();
        var wifi = devices.FirstOrDefault(d => d.Serial.Contains("._adb-tls-"));
        var usb = devices.FirstOrDefault(d => wifi is not null && wifi.Serial.StartsWith($"adb-{d.Serial}-"));
        if (usb.Serial is null || wifi.Serial is null)
            Assert.Inconclusive("A device connected over both USB and Wi-Fi is required.");

        var folder = Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_FOLDER") ?? "/sdcard/Download";
        var largest = int.Parse(Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_COUNT") ?? "12", CultureInfo.InvariantCulture);
        var smallest = int.Parse(Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_SMALL_COUNT") ?? "374", CultureInfo.InvariantCulture);

        // ADB_EXPLORER_BENCH_ONLY picks the runs, by part of their dataset, lanes or method
        var only = Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        (string Name, FileClass[] Files)[] datasets =
        [
            ($"{largest} largest", ListFiles(usb, folder, largest, largestFirst: true)),
            ($"{smallest} smallest", ListFiles(usb, folder, smallest, largestFirst: false)),
        ];

        (string Name, LogicalDeviceViewModel Device, int Lanes)[] laneSetups =
        [
            ("USB", BenchViewModel(usb, null), 1),
            ("USB + Wi-Fi", BenchViewModel(usb, wifi), 2),
        ];

        List<(string Dataset, string Lanes, string Method, string Seconds, double? MBps, long PeakMB, long SpillMB)> results = [];
        var local = Path.Combine(Path.GetTempPath(), $"adb-explorer-bench-{Guid.NewGuid():N}");

        try
        {
            foreach (var (dataset, files) in datasets)
            {
                var bytes = files.Sum(f => f.Size ?? 0);

                foreach (var (lanes, device, laneCount) in laneSetups)
                {
                    List<(string Method, Func<double> Run)> methods =
                    [
                        ("Stream", () => StreamFiles(device, files, local)),
                        ("Direct pull", () => PullFiles(device, files, local)),
                    ];

                    var expected = bytes / (laneCount * 30.0 * 1048576) + files.Length * 0.02;
                    ReadAheadRunLimit = TimeSpan.FromSeconds(Math.Max(5, expected * 1.1));

                    foreach (var (method, run) in methods)
                    {
                        if (only is not null && !only.Any(o => $"{dataset} | {lanes} | {method}".Contains(o, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        Directory.CreateDirectory(local);

                        long peak = 0;
                        double seconds = 0;
                        string? failure = null;
                        var spill = MeasurePeakSpill(() => RunSta(() =>
                        {
                            try
                            {
                                seconds = MeasurePeak(run, out peak);
                            }
                            catch (AssertFailedException e)
                            {
                                failure = e.Message;
                            }
                        }));

                        if (failure is null)
                            results.Add((dataset, lanes, method, $"{seconds:F2}", bytes / 1048576.0 / seconds, peak, spill));
                        else
                            results.Add((dataset, lanes, method, failure, null, peak, spill));

                        Directory.Delete(local, true);
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(local))
                Directory.Delete(local, true);

            var report = new StringBuilder("| Dataset | Lanes | Method | Seconds | MB/s | Peak private MB | Peak spill MB |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in results)
                report.AppendLine($"| {r.Dataset} | {r.Lanes} | {r.Method} | {r.Seconds} | {r.MBps:F1} | {r.PeakMB} | {r.SpillMB} |");

            foreach (var (dataset, files) in datasets)
                report.AppendLine($"\n{dataset}: {files.Length} files, {files.Sum(f => f.Size ?? 0) / 1048576.0:F0} MB");

            Console.WriteLine(report);
        }
    }

    /// <summary>The <paramref name="count"/> largest or smallest files directly in <paramref name="folder"/>, by name as Explorer orders a selection.</summary>
    private static FileClass[] ListFiles(DeviceData device, string folder, int count, bool largestFirst)
    {
        var pick = largestFirst ? $"sort -n | tail -{count}" : $"sort -n | head -{count}";
        var listing = Shell(device, $"find '{folder}' -maxdepth 1 -type f -exec stat -c '%s %Y %n' {{}} + | {pick}");

        return [.. listing.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(' ', 3))
            .Select(parts => new FileClass(FileHelper.GetFullName(parts[2]), parts[2], FileType.File,
                size: long.Parse(parts[0], CultureInfo.InvariantCulture),
                modifiedTime: DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[1], CultureInfo.InvariantCulture)).LocalDateTime))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>As a drag of <paramref name="files"/> dropped in Explorer: one batch op, its files streamed in the shell's order.</summary>
    private static double StreamFiles(LogicalDeviceViewModel device, FileClass[] files, string localParent)
    {
        var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
        List<FileClass.ShellPullItem> items = [.. files.Select(f => new FileClass.ShellPullItem(f, new SyncFile(f), [f.FullPath], null, null, null))];
        var op = VirtualFileDataObject.CreateShellPulls(vfdo, items, device, Dispatcher.CurrentDispatcher).Single();

        vfdo.SetFileDescriptors([.. files.Select(f => new FileDescriptor
        {
            Name = f.FullName,
            Length = f.Size,
            ChangeTimeUtc = f.ModifiedTime?.ToUniversalTime(),
            Stream = () => op.OpenShellStream(f.FullPath, f.Size),
        })]);

        op.Start();

        var sw = Stopwatch.StartNew();
        var dropTarget = GetDropTarget(localParent);
        uint effect = 1; // DROPEFFECT_COPY
        dropTarget.DragEnter(vfdo, 1, default, ref effect);
        effect = 1;
        dropTarget.Drop(vfdo, 0, default, ref effect);

        try
        {
            PumpUntil(() => op.Status is not FileOperation.OperationStatus.InProgress && IsCopied(localParent, files.Length), ReadAheadRunLimit);
        }
        catch (AssertFailedException)
        {
            // Not to slow down the next runs
            op.Cancel();
            throw;
        }

        sw.Stop();

        Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
        Assert.IsEmpty(op.FailedFiles);

        return sw.Elapsed.TotalSeconds;
    }

    /// <summary>The app's own pull of <paramref name="files"/> to a Windows folder.</summary>
    private static double PullFiles(LogicalDeviceViewModel device, FileClass[] files, string localParent)
    {
        var target = new SyncFile(localParent, FileType.Folder) { PathType = FilePathType.Windows };
        var op = FileSyncOperation.SyncBatch(FileOperation.OperationType.Pull, [.. files.Select(f => new SyncFile(f))], target, device, Dispatcher.CurrentDispatcher);

        var sw = Stopwatch.StartNew();
        op.Start();
        try
        {
            PumpUntil(() => op.Status is not FileOperation.OperationStatus.InProgress, ReadAheadRunLimit);
        }
        catch (AssertFailedException)
        {
            op.Cancel();
            throw;
        }

        sw.Stop();

        Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
        Assert.IsEmpty(op.FailedFiles);
        Assert.IsTrue(IsCopied(localParent, files.Length));

        return sw.Elapsed.TotalSeconds;
    }

    /// <summary>The most the pipes' spill files held together while <paramref name="run"/> ran, in MB.</summary>
    private static long MeasurePeakSpill(Action run)
    {
        long peak = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            while (!sampling.IsCancellationRequested)
            {
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "ADB Explorer spill *"))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // Deleted meanwhile
                    }
                }

                peak = Math.Max(peak, total);
                Thread.Sleep(50);
            }
        }) { IsBackground = true };

        sampler.Start();
        run();
        sampling.Cancel();
        sampler.Join();

        return peak / 1048576;
    }

    private static void PumpUntil(Func<bool> isDone, TimeSpan limit)
    {
        var sw = Stopwatch.StartNew();
        while (!isDone())
        {
            if (sw.Elapsed > limit)
                Assert.Fail($"Not done within {limit.TotalSeconds} s");

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }
    }
}
