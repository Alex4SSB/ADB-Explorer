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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using static ADB_Explorer.Models.AbstractFile;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Test;

/// <summary>
/// Timings of a device folder pulled to Windows: streamed to the shell, with and without read-ahead memory,
/// and 7-Zip's way (pulled in parallel to temp, then moved). Opt in with ADB_EXPLORER_BENCH=1, and
/// ADB_EXPLORER_BENCH_DRIVE=F:\ for a destination on another drive. Run: dotnet test --filter ShellStream_Benchmark
/// </summary>
public partial class ShellStreamTests
{
    private const string BenchRoot = "/sdcard/adb-explorer-bench";
    private const int SmallFileCount = 1500;
    private const int LargeFileCount = 3;
    private const int ReadAheadMB = 1024;

    private record BenchResult(string Dataset, string Lanes, string Method, double Seconds, long Bytes, long PeakPrivateMB);

    [TestMethod]
    public void ShellStream_Benchmark()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_BENCH=1 to run the benchmark.");

        if (_device is null || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        var usb = _device!;
        var wifi = new AdbClient().GetDevices()
            .FirstOrDefault(d => d.State is DeviceState.Online && d.Serial.StartsWith($"adb-{usb.Serial}-") && d.Serial.Contains("._adb-tls-"));
        var otherDrive = Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_DRIVE");

        (string Name, string Folder, int Count, string Create)[] datasets =
        [
            ("1500 small files", $"{BenchRoot}/small", SmallFileCount,
                $"i=1; while [ $i -le {SmallFileCount} ]; do head -c $((4096 + RANDOM % 61440)) /dev/urandom > f$i.bin; i=$((i+1)); done"),
            ("3 × 200 MB", $"{BenchRoot}/large", LargeFileCount,
                $"for i in 1 2 3; do head -c 209715200 /dev/urandom > big$i.bin; done"),
        ];

        List<(string Name, LogicalDeviceViewModel Device)> laneSetups = [("USB", BenchViewModel(usb, null))];
        if (wifi is not null)
            laneSetups.Add(("USB + Wi-Fi", BenchViewModel(usb, wifi)));

        var localC = Path.Combine(Path.GetTempPath(), $"adb-explorer-bench-{Guid.NewGuid():N}");
        var localOther = otherDrive is null ? null : Path.Combine(otherDrive, $"adb-explorer-bench-{Guid.NewGuid():N}");
        List<BenchResult> results = [];

        try
        {
            foreach (var dataset in datasets)
            {
                if (Shell(usb, $"ls {dataset.Folder} 2>/dev/null | wc -l") != dataset.Count.ToString())
                    Shell(usb, $"rm -rf {dataset.Folder}; mkdir -p {dataset.Folder} && cd {dataset.Folder} && {dataset.Create}");

                var bytes = long.Parse(Shell(usb, $"find {dataset.Folder} -type f -exec stat -c %s {{}} + | awk '{{s+=$1}} END {{print s}}'"));

                foreach (var (lanes, device) in laneSetups)
                {
                    List<(string Method, Func<double> Run)> methods = [];

                    // As before: each file through its own small buffer, 4 files ahead, over one connection
                    if (lanes == "USB")
                        methods.Add(("Stream, 4 ahead, no memory", () => Stream(device, usb.Serial, dataset.Folder, localC, dataset.Count, 0, 5)));

                    methods.Add(($"Stream, {ReadAheadMB} MB read-ahead", () => Stream(device, usb.Serial, dataset.Folder, localC, dataset.Count, ReadAheadMB, null)));
                    methods.Add(("Pull to temp + move (same drive)", () => PullThenMove(device, usb.Serial, dataset.Folder, localC, localC)));
                    if (localOther is not null)
                        methods.Add(("Pull to temp + copy (other drive)", () => PullThenMove(device, usb.Serial, dataset.Folder, localC, localOther)));

                    foreach (var (method, run) in methods)
                    {
                        Directory.CreateDirectory(localC);
                        if (localOther is not null)
                            Directory.CreateDirectory(localOther);

                        long peak = 0;
                        double seconds = 0;
                        RunSta(() => seconds = MeasurePeak(run, out peak));
                        results.Add(new(dataset.Name, lanes, method, seconds, bytes, peak));

                        Directory.Delete(localC, true);
                        if (localOther is not null)
                            Directory.Delete(localOther, true);
                    }
                }
            }
        }
        finally
        {
            Data.Settings.TransferMemoryMB = AppSettings.TransferMemoryMBDefault;
            Shell(usb, $"rm -rf {BenchRoot}");

            foreach (var dir in new[] { localC, localOther }.OfType<string>().Where(Directory.Exists))
                Directory.Delete(dir, true);

            var report = new StringBuilder("| Dataset | Lanes | Method | Seconds | MB/s | Peak private MB |\n|---|---|---|---|---|---|\n");
            foreach (var r in results)
                report.AppendLine($"| {r.Dataset} | {r.Lanes} | {r.Method} | {r.Seconds:F1} | {r.Bytes / 1048576.0 / r.Seconds:F1} | {r.PeakPrivateMB} |");

            var reportPath = Path.Combine(Path.GetTempPath(), "adb-explorer-bench.md");
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine(report);
            Console.WriteLine(reportPath);
        }
    }

    /// <summary>
    /// The shell's own cost per file, with no device: 1500 small files copied from our pipe streams (filled up front),
    /// from Windows' memory streams, and from files on disk. Opt in with ADB_EXPLORER_BENCH=1.
    /// </summary>
    [TestMethod]
    public void ShellStream_ShellFloorBenchmark()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_BENCH=1 to run the benchmark.");

        var random = new Random(1500);
        byte[][] contents = [.. Enumerable.Range(0, SmallFileCount).Select(_ =>
        {
            var data = new byte[4096 + random.Next(61440)];
            random.NextBytes(data);
            return data;
        })];

        var work = Path.Combine(Path.GetTempPath(), $"adb-explorer-bench-{Guid.NewGuid():N}");
        var source = Path.Combine(work, "source", "small");
        Directory.CreateDirectory(source);
        for (var i = 0; i < contents.Length; i++)
            File.WriteAllBytes(Path.Combine(source, $"f{i + 1}.bin"), contents[i]);

        List<(string Method, double Seconds)> results = [];

        try
        {
            // ADB_EXPLORER_BENCH_ONLY picks the methods to run, by part of their name
            var only = Environment.GetEnvironmentVariable("ADB_EXPLORER_BENCH_ONLY");
            bool Runs(string method) => only is null || method.Contains(only, StringComparison.OrdinalIgnoreCase);

            for (var round = 1; round <= 2; round++)
            {
                if (Runs("pipe"))
                    results.Add(("Our pipe streams (no device)", ShellCopy(work, target => CopyVirtual(target, contents, data =>
                {
                    var pipe = new SyncPipe(data.Length);
                    pipe.Write(data);
                    pipe.Complete();

                    return new DevicePullStream(pipe, data.Length).ToComPointer();
                }))));

                if (Runs("memory"))
                    results.Add(("Windows memory streams", ShellCopy(work, target => CopyVirtual(target, contents, data => SHCreateMemStream(data, (uint)data.Length)))));

                if (Runs("disk"))
                    results.Add(("Files on disk", ShellCopy(work, target =>
                {
                    var dropTarget = GetDropTarget(target);
                    var dataObject = ShellDataObject(source);
                    uint effect = 1; // DROPEFFECT_COPY
                    dropTarget.DragEnter(dataObject, 1, default, ref effect);
                    effect = 1;
                    dropTarget.Drop(dataObject, 0, default, ref effect);
                })));
            }
        }
        finally
        {
            Directory.Delete(work, true);

            var report = new StringBuilder("| Method | Seconds | ms per file |\n|---|---|---|\n");
            foreach (var (method, seconds) in results)
                report.AppendLine($"| {method} | {seconds:F2} | {seconds * 1000 / SmallFileCount:F2} |");

            Console.WriteLine(report);
        }
    }

    /// <summary>Seconds from <paramref name="drop"/> until the shell finished copying, into a fresh folder.</summary>
    private static double ShellCopy(string work, Action<string> drop)
    {
        var target = Path.Combine(work, $"target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);

        double seconds = 0;
        RunSta(() =>
        {
            var sw = Stopwatch.StartNew();
            drop(target);

            try
            {
                PumpUntil(() => IsCopied(Path.Combine(target, "small"), SmallFileCount));
            }
            catch (AssertFailedException e)
            {
                var copied = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet();
                var missing = Enumerable.Range(1, SmallFileCount).Select(i => $"f{i}.bin").Where(name => !copied.Contains(name));
                Assert.Fail($"{e.Message}: {copied.Count} files, missing: {string.Join(", ", missing.Take(10))}");
            }

            seconds = sw.Elapsed.TotalSeconds;
        });

        Directory.Delete(target, true);

        return seconds;
    }

    private static void CopyVirtual(string target, byte[][] contents, Func<byte[], nint> openStream)
    {
        var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
        vfdo.SetFileDescriptors([
            // FileContents' indexes skip descriptors without a stream, so folders get one, as the app's do
            new FileDescriptor { Name = "small", IsDirectory = true, Stream = () => 0 },
            .. contents.Select((data, i) => new FileDescriptor
            {
                Name = $"small\\f{i + 1}.bin",
                Length = data.Length,
                ChangeTimeUtc = DateTime.UtcNow,
                Stream = () => openStream(data),
            }),
        ]);

        var dropTarget = GetDropTarget(target);
        uint effect = 1; // DROPEFFECT_COPY
        dropTarget.DragEnter(vfdo, 1, default, ref effect);
        effect = 1;
        dropTarget.Drop(vfdo, 0, default, ref effect);
    }

    private static ComDataObject ShellDataObject(string path)
    {
        var itemId = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, 0, ref itemId, out var item);

        var bhid = new Guid("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4"); // BHID_DataObject
        var dataObjectId = typeof(ComDataObject).GUID;
        item.BindToHandler(0, ref bhid, ref dataObjectId, out var pointer);

        var dataObject = (ComDataObject)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);

        return dataObject;
    }

    [DllImport("shlwapi.dll")]
    private static extern nint SHCreateMemStream(byte[] pInit, uint cbInit);

    /// <summary>The time <paramref name="run"/> takes, and the most private memory the process held meanwhile.</summary>
    private static double MeasurePeak(Func<double> run, out long peakMB)
    {
        long peak = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            using var process = Process.GetCurrentProcess();
            while (!sampling.IsCancellationRequested)
            {
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
                Thread.Sleep(50);
            }
        }) { IsBackground = true };

        GC.Collect();
        sampler.Start();
        var seconds = run();
        sampling.Cancel();
        sampler.Join();

        peakMB = peak / 1048576;

        return seconds;
    }

    private static double Stream(LogicalDeviceViewModel device, string serial, string remoteFolder, string localParent, int count, int readAheadMB, int? maxThreads)
    {
        Data.Settings.TransferMemoryMB = readAheadMB;

        var (folderClass, tree, items) = BenchTree(serial, remoteFolder);
        List<FileClass.ShellPullItem> pullItems =
            [new(folderClass, new SyncFile(folderClass, tree), [.. items.Where(i => !i.IsFolder).Select(i => i.Name)], null, null, null)];

        var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
        var op = VirtualFileDataObject.CreateShellPulls(vfdo, pullItems, device, Dispatcher.CurrentDispatcher).Single();
        op.MaxThreads = maxThreads;

        var parent = FileHelper.GetParentPath(remoteFolder);
        vfdo.SetFileDescriptors(items.Select(item => new FileDescriptor
        {
            Name = FileHelper.ExtractRelativePath(item.Name, parent).Replace('/', '\\'),
            IsDirectory = item.IsFolder,
            Length = item.Size,
            ChangeTimeUtc = item.Date.FromUnixTime(),
            Stream = () => item.IsFolder ? 0 : op.OpenShellStream(item.Name, item.Size),
        }).ToList());

        op.Start();

        var sw = Stopwatch.StartNew();
        var dropTarget = GetDropTarget(localParent);
        uint effect = 1; // DROPEFFECT_COPY
        dropTarget.DragEnter(vfdo, 1, default, ref effect);
        effect = 1;
        dropTarget.Drop(vfdo, 0, default, ref effect);

        var local = Path.Combine(localParent, folderClass.FullName);
        PumpUntil(() => op.Status is not FileOperation.OperationStatus.InProgress && IsCopied(local, count));
        sw.Stop();

        Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
        Assert.IsEmpty(op.FailedFiles);
        Assert.AreEqual(0, TransferMemory.Reserved, "All read-ahead memory went back");

        return sw.Elapsed.TotalSeconds;
    }

    /// <summary>7-Zip's way: everything pulled to a temp folder first (here in parallel), which Explorer then moves to the drop target.</summary>
    private static double PullThenMove(LogicalDeviceViewModel device, string serial, string remoteFolder, string tempParent, string destinationParent)
    {
        var (folderClass, tree, _) = BenchTree(serial, remoteFolder);
        // A pull's target is where the folder itself goes
        var tempTarget = new SyncFile(Path.Combine(tempParent, "staging", folderClass.FullName), FileType.Folder) { PathType = FilePathType.Windows };

        var op = FileSyncOperation.PullFile(new SyncFile(folderClass, tree), tempTarget, device, Dispatcher.CurrentDispatcher);

        var sw = Stopwatch.StartNew();
        op.Start();
        PumpUntil(() => op.Status is not FileOperation.OperationStatus.InProgress);

        Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
        Assert.IsEmpty(op.FailedFiles);

        var source = tempTarget.FullPath;
        var destination = Path.Combine(destinationParent, folderClass.FullName);
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, destination);
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }

            Directory.Delete(source, true);
        }

        sw.Stop();

        return sw.Elapsed.TotalSeconds;
    }

    private static (FileClass Folder, IEnumerable<FolderTree> Tree, FolderTree[] Items) BenchTree(string serial, string remoteFolder)
    {
        var tree = FileHelper.GetFolderTree([remoteFolder], deviceId: serial);
        FolderTree[] items = [new(remoteFolder, null, null), .. tree];

        return (new FileClass(FileHelper.GetFullName(remoteFolder), remoteFolder, FileType.Folder), tree, items);
    }

    private static LogicalDeviceViewModel BenchViewModel(DeviceData usb, DeviceData? wifi)
    {
        var usbSnapshot = new DeviceSnapshot(usb.Serial, usb.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", usb);
        var device = LogicalDevice.From(usbSnapshot);

        if (wifi is not null)
        {
            var wifiSnapshot = new DeviceSnapshot(wifi.Serial, wifi.Model, DeviceStatus.Ok, DeviceType.Service, RootStatus.Unchecked, "", wifi);
            device.SetConnections([DeviceConnection.From(usbSnapshot), DeviceConnection.From(wifiSnapshot)]);
        }

        return new(device);
    }

    // The slowest benchmark copy took 15.7 s; a hang fails a little after that
    private static readonly TimeSpan CopyLimit = TimeSpan.FromSeconds(18);

    private static void PumpUntil(Func<bool> isDone)
    {
        var sw = Stopwatch.StartNew();
        while (!isDone())
        {
            if (sw.Elapsed > CopyLimit)
                Assert.Fail($"Not done within {CopyLimit.TotalSeconds} s");

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }
    }

    /// <summary>All <paramref name="count"/> files are there, and the shell let go of them.</summary>
    private static bool IsCopied(string root, int count)
    {
        try
        {
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
            if (files.Count != count)
                return false;

            foreach (var file in files)
            {
                using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
