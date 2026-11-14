#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ADB_Test;

/// <summary>Files opened through the mount the way File Explorer and the apps it starts do: several readers at once, and reads abandoned midway.</summary>
[TestClass]
public class DeviceMountStressTests
{
    private const string Root = "/sdcard/adbx-mount-stress";

    private const int FileCount = 12;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);
    }

    [TestMethod]
    [Timeout(17_000)]
    public void Mount_ConcurrentAndAbandonedReads()
    {
        List<DeviceData> devices;
        try
        {
            devices = [.. new AdbClient().GetDevices().Where(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"))];
        }
        catch
        {
            devices = [];
        }

        if (devices.Count == 0 || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        var report = new StringBuilder();
        foreach (var device in devices)
        {
            var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
                new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

            try
            {
                Shell(device, $"rm -rf {Root}; mkdir -p {Root} && cd {Root} && for i in $(seq 1 {FileCount}); do head -c $((i*400000+123)) /dev/urandom > f$i.bin; done; head -c 30000000 /dev/urandom > big.bin");
                var deviceHashes = Shell(device, $"cd {Root} && md5sum *.bin").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split("  ")).ToDictionary(p => p[1].Trim(), p => p[0]);

                var local = DeviceMount.MountAndMap(viewModel, Root)!;
                Cmd($"dir /b \"{local}\"");

                // The app's thread pool tied up (its threads waiting on the UI): downloads must not wait for it
                var busyTime = WithBusyThreadPool(() =>
                {
                    var readers = Enumerable.Range(1, 6)
                        .Select(i => Process.Start(new ProcessStartInfo("cmd.exe", $"/c type \"{Path.Combine(local, $"f{i}.bin")}\" > nul") { CreateNoWindow = true, UseShellExecute = false })!)
                        .ToList();

                    foreach (var reader in readers)
                        reader.WaitForExit();
                });
                report.AppendLine($"{device.Model}: 6 files opened with a busy thread pool in {busyTime} ms");
                Assert.IsLessThan(5000L, busyTime, "downloads waited on the thread pool");

                // A thumbnailer's read abandoned midway while the app's open waits on the same download
                var bigFile = Path.Combine(local, "big.bin");
                var abandoned = Process.Start(new ProcessStartInfo("cmd.exe", $"/c type \"{bigFile}\" > nul") { CreateNoWindow = true, UseShellExecute = false })!;
                System.Threading.Thread.Sleep(150);
                var bigCopy = Path.Combine(Path.GetTempPath(), $"adbx-stress-{Guid.NewGuid():N}.bin");
                var waiting = Process.Start(new ProcessStartInfo("cmd.exe", $"/c copy /y \"{bigFile}\" \"{bigCopy}\"") { CreateNoWindow = true, UseShellExecute = false })!;
                System.Threading.Thread.Sleep(150);
                abandoned.Kill(true);
                waiting.WaitForExit();
                var failures = new List<string>();
                if (!File.Exists(bigCopy))
                    failures.Add("big.bin race: the waiting open failed when the other was abandoned");
                else if (Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(bigCopy))) != deviceHashes["big.bin"])
                    failures.Add($"big.bin race: wrong content, {new FileInfo(bigCopy).Length} bytes");

                File.Delete(bigCopy);

                // Three readers per file at once, like a thumbnailer, a property handler and the app opening it
                var watch = Stopwatch.StartNew();
                var reads = deviceHashes.Keys.SelectMany(name => Enumerable.Range(0, 3).Select(n => (name, n))).ToList();
                Parallel.ForEach(reads, new ParallelOptions { MaxDegreeOfParallelism = 12 }, read =>
                {
                    var copy = Path.Combine(Path.GetTempPath(), $"adbx-stress-{Guid.NewGuid():N}.bin");
                    try
                    {
                        var output = Cmd($"copy /y \"{Path.Combine(local, read.name)}\" \"{copy}\"");
                        if (!File.Exists(copy))
                        {
                            lock (failures) failures.Add($"{read.name}#{read.n}: {output}");
                            return;
                        }

                        var hash = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(copy)));
                        if (hash != deviceHashes[read.name])
                            lock (failures) failures.Add($"{read.name}#{read.n}: wrong content, {new FileInfo(copy).Length} bytes");
                    }
                    finally
                    {
                        File.Delete(copy);
                    }
                });

                report.AppendLine($"{device.Model}: {reads.Count} reads in {watch.ElapsedMilliseconds} ms, {failures.Count} failed");
                foreach (var failure in failures)
                    report.AppendLine($"  {failure}");
            }
            finally
            {
                DeviceMount.Unmount();
                Shell(device, $"rm -rf {Root}");
            }
        }

        Console.WriteLine(report);
        Assert.DoesNotContain("failed\n  ", report.ToString().Replace("\r", ""), report.ToString());
    }

    /// <summary>Times an action while every thread-pool thread is blocked, and the pool can only add more slowly.</summary>
    private static long WithBusyThreadPool(Action action)
    {
        System.Threading.ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
        System.Threading.ThreadPool.SetMinThreads(Environment.ProcessorCount, minIo);
        // Not disposed: the blocked threads may still be waking on it after this returns
        var release = new System.Threading.ManualResetEventSlim();
        try
        {
            for (var i = 0; i < Environment.ProcessorCount * 2; i++)
                System.Threading.ThreadPool.QueueUserWorkItem(_ => release.Wait());

            System.Threading.Thread.Sleep(200);
            var watch = Stopwatch.StartNew();
            action();

            return watch.ElapsedMilliseconds;
        }
        finally
        {
            release.Set();
            System.Threading.ThreadPool.SetMinThreads(minWorkers, minIo);
        }
    }

    private static string Cmd(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return output.Trim();
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
