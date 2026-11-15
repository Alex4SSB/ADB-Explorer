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
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Test;

/// <summary>
/// With the app setting on, files whose names a target refuses land there with lookalike characters:
/// pulled to Windows, and copied onto a drive with restricted naming. Runs on each USB device.
/// </summary>
[TestClass]
public class IllegalNameSubstitutionTests
{
    private static string N(string name) => TestNames.Pseudo(name);

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);

        // Device-side copies run as the app's operations: they need its queue, and a UI dispatcher to add to it on
        using var ready = new ManualResetEventSlim();
        var ui = new Thread(() =>
        {
            typeof(ADB_Explorer.App).GetProperty(nameof(ADB_Explorer.App.AppDispatcher))!.SetValue(null, Dispatcher.CurrentDispatcher);
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ready.Wait();
        Data.FileOpQ ??= new();
    }

    [TestMethod]
    [Timeout(1_000)]
    public void Pull_GivesLookalikesForNamesWindowsRefuses()
    {
        var devices = Devices();
        var substitute = Data.Settings.SubstituteIllegalNames;
        try
        {
            Data.Settings.SubstituteIllegalNames = true;
            foreach (var device in devices)
            {
                var viewModel = ViewModel(device);
                var folder = $"{AdbExplorerConst.TEMP_PATH}/{N("adbx-subst")}-{Guid.NewGuid():N}";
                var localParent = Path.Combine(Path.GetTempPath(), $"adbx-subst-{Guid.NewGuid():N}");
                Directory.CreateDirectory(localParent);

                // Device names, nested one included, by their content; the temp drive is ext4, which takes them all.
                // Not a backslash: device paths are joined with FileHelper.ConcatPaths, which reads it as a separator.
                Shell(device, $"""
                    mkdir -p {folder}/'q?d' && cd {folder} && echo 0 > 'a:b.txt' && echo 1 > CON.txt && echo 2 > 'trail.' \
                    && echo 3 > 'q?d/x*y.txt' && echo 5 > 'real：fw.txt'
                    """.ReplaceLineEndings("\n"));
                try
                {
                    var folderClass = new FileClass(FileHelper.GetFullName(folder), folder, FileType.Folder);
                    var source = new SyncFile(folderClass, FileHelper.GetFolderTree([folder], deviceId: device.Serial));
                    var op = FileSyncOperation.PullFile(source, SyncFile.MergeToWindowsPath(source, localParent), viewModel, Dispatcher.CurrentDispatcher);
                    op.Start();
                    PumpUntil(() => op.Status is not (FileOperation.OperationStatus.None or FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress), TimeSpan.FromSeconds(8));
                    Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status, device.Model);
                    Assert.AreEqual(0, op.FailedFiles.Count(), $"{device.Model}: {string.Join(" | ", op.FailedFiles.Select(f => f.FullPath))}");

                    var local = Path.Combine(localParent, FileHelper.GetFullName(folder));
                    var expected = new Dictionary<string, string>
                    {
                        ["a：b.txt"] = "0",
                        ["ＣON.txt"] = "1",
                        ["trail．"] = "2",
                        [@"q？d\x＊y.txt"] = "3",
                        ["real：fw.txt"] = "5",
                    };

                    var pulled = Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(local, f)).ToList();
                    CollectionAssert.AreEquivalent(expected.Keys.ToList(), pulled, $"{device.Model}: {string.Join(" | ", pulled)}");
                    foreach (var (name, content) in expected)
                        Assert.AreEqual(content, File.ReadAllText(Path.Combine(local, name)).Trim(), $"{device.Model}: {name}");
                }
                finally
                {
                    Shell(device, $"rm -rf {folder}");
                    Directory.Delete(localParent, true);
                }
            }
        }
        finally
        {
            Data.Settings.SubstituteIllegalNames = substitute;
        }
    }

    [TestMethod]
    [Timeout(1_000)]
    public void Copy_ToRestrictedDriveGivesLookalikes()
    {
        var devices = Devices();
        var substitute = Data.Settings.SubstituteIllegalNames;
        try
        {
            Data.Settings.SubstituteIllegalNames = true;
            foreach (var device in devices)
            {
                var viewModel = ViewModel(device);

                // Android mounts its FUSE storage noexec, which the app reads as restricted naming
                var internalStorage = viewModel.Drives.First(drive => drive.Type is AbstractDrive.DriveType.Internal);
                internalStorage.FSInfo = new(string.Empty, "/sdcard", "fuse", ["rw", "nosuid", "nodev", "noexec"]);

                var source = $"{AdbExplorerConst.TEMP_PATH}/{N("adbx-subst")}-{Guid.NewGuid():N}";
                var target = $"/sdcard/{N("adbx-subst")}-{Guid.NewGuid():N}";
                Shell(device, $"mkdir -p {source} {target} && echo 0 > '{source}/a:b.txt'");
                try
                {
                    var item = new FileClass("a:b.txt", $"{source}/a:b.txt", FileType.File);
                    var operations = ShellFileOperation.MoveItems(viewModel, [item], target, source, Array.Empty<string>(), ADB_Explorer.App.AppDispatcher, DragDropEffects.Copy);
                    var watch = Stopwatch.StartNew();
                    while (operations.Any(op => op.Status is FileOperation.OperationStatus.None or FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress)
                        && watch.Elapsed < TimeSpan.FromSeconds(8))
                    {
                        Thread.Sleep(50);
                    }

                    Assert.IsTrue(operations.All(op => op.Status is FileOperation.OperationStatus.Completed), $"{device.Model}: {string.Join(", ", operations.Select(op => op.Status))}");
                    CollectionAssert.AreEqual(new[] { "a：b.txt" }, Lines(Shell(device, $"ls -1 {target}")), device.Model);
                    Assert.AreEqual("0", Shell(device, $"cat '{target}/a：b.txt'"), device.Model);
                }
                finally
                {
                    Shell(device, $"rm -rf {source} {target}");
                }
            }
        }
        finally
        {
            Data.Settings.SubstituteIllegalNames = substitute;
        }
    }

    private static List<DeviceData> Devices()
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

        return devices;
    }

    private static LogicalDeviceViewModel ViewModel(DeviceData device) => new(LogicalDevice.From(
        new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

    private static void PumpUntil(Func<bool> isDone, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (!isDone())
        {
            if (watch.Elapsed > limit)
                Assert.Fail($"Not done within {limit.TotalSeconds} s");

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }
    }

    private static List<string> Lines(string output) => [.. output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
