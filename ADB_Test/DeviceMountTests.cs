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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ADB_Test;

/// <summary>
/// The read-only Cloud Files mount: folders listed from the device, files pulled on open, deletes refused, cleanup on unmount.
/// Mounts each USB device in turn, under the user's profile.
/// </summary>
[TestClass]
public class DeviceMountTests
{
    private const string Root = "/sdcard/adbx-mount-test";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);
    }

    [TestMethod]
    [Timeout(120_000)]
    public void Mount_ListsReadsAndRefusesDeletes()
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

        var notes = new StringBuilder();
        foreach (var device in devices)
        {
            var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
                new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

            string? local = null;
            string? windowRoot = null;
            var createdTrash = false;
            try
            {
                // The recycle bin is only created here if the device has none, and only that one is removed after
                createdTrash = Shell(device, $"[ -d {AdbExplorerConst.RECYCLE_PATH} ] || {{ mkdir {AdbExplorerConst.RECYCLE_PATH} && echo created; }}") == "created";
                Shell(device, $"rm -rf {Root}; mkdir -p {Root}/sub/deeper {Root}/.hidden-dir && cd {Root} && head -c 3000000 /dev/urandom > a.bin "
                    + "&& echo hello > sub/b.txt && echo 1 > Case.txt && echo h > .hidden.txt; echo x > 'bad:name.txt'");

                local = DeviceMount.MountAndMap(viewModel, Root);
                Assert.IsNotNull(local);
                notes.AppendLine($"{device.Model}: shell registration {DeviceMount.Current!.IsShellRegistered}, folder {local}");

                // The platform doesn't fetch for the provider's own process, so look through another one
                var names = Lines(Cmd($"dir /a /b \"{local}\""));
                CollectionAssert.Contains(names, "a.bin", device.Model);
                CollectionAssert.Contains(names, "sub");
                CollectionAssert.DoesNotContain(names, "bad:name.txt");
                Assert.AreEqual(1, names.Count(n => string.Equals(n, "case.txt", StringComparison.OrdinalIgnoreCase)), string.Join(", ", names));

                // Dot entries are there, but hidden from a plain listing
                CollectionAssert.IsSubsetOf(new[] { ".hidden.txt", ".hidden-dir" }, names);
                var visible = Lines(Cmd($"dir /b \"{local}\""));
                CollectionAssert.DoesNotContain(visible, ".hidden.txt");
                CollectionAssert.DoesNotContain(visible, ".hidden-dir");
                CollectionAssert.Contains(visible, "a.bin");

                var mountRoot = DeviceMount.Current.LocalRoot;
                CollectionAssert.DoesNotContain(Lines(Cmd($"dir /a /b \"{mountRoot}\"")), AdbExplorerConst.RECYCLE_FOLDER, "the recycle bin is left out");

                Assert.AreEqual(3000000L, new FileInfo(Path.Combine(local, "a.bin")).Length);

                // Pulled on open, chunk by chunk
                var copy = Path.Combine(Path.GetTempPath(), $"adbx-mount-{Guid.NewGuid():N}.bin");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Cmd($"copy /y \"{Path.Combine(local, "a.bin")}\" \"{copy}\"");
                notes.AppendLine($"  3 MB hydrated in {watch.ElapsedMilliseconds} ms");

                var localHash = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(copy)));
                File.Delete(copy);
                var deviceHash = Shell(device, $"md5sum {Root}/a.bin").Split(' ')[0];
                Assert.AreEqual(deviceHash, localHash, device.Model);

                Assert.AreEqual("hello", Cmd($"type \"{Path.Combine(local, "sub", "b.txt")}\""));
                Assert.AreEqual("deeper", Cmd($"dir /b \"{Path.Combine(local, "sub")}\"").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First(n => n == "deeper"));

                var deleteOutput = Cmd($"del /f \"{Path.Combine(local, "sub", "b.txt")}\"");
                notes.AppendLine($"  delete: {deleteOutput}");
                Assert.IsTrue(File.Exists(Path.Combine(local, "sub", "b.txt")), deleteOutput);
                Assert.AreEqual("hello", Shell(device, $"cat {Root}/sub/b.txt"), "the device file must survive");

                // A File Explorer window on the mount is closed with it
                System.Diagnostics.Process.Start("explorer.exe", local);
                Assert.IsTrue(WaitFor(() => ExplorerWindowsUnder(mountRoot) > 0, 10), "File Explorer didn't open the mount");
                windowRoot = mountRoot;
            }
            finally
            {
                DeviceMount.Unmount();
                Shell(device, $"rm -rf {Root}");
                if (createdTrash)
                    Shell(device, $"rmdir {AdbExplorerConst.RECYCLE_PATH}");
            }

            Assert.IsNull(DeviceMount.Current);
            Assert.IsFalse(Directory.Exists(local), $"{local} should be removed on unmount");
            if (windowRoot is not null)
                Assert.IsTrue(WaitFor(() => ExplorerWindowsUnder(windowRoot) == 0, 5), "the File Explorer window should close on unmount");
        }

        Console.WriteLine(notes);
    }

    [TestMethod]
    [Timeout(30_000)]
    public void Mount_LeftoverPlaceholdersAreCleanedUp()
    {
        DeviceData? device;
        try
        {
            device = new AdbClient().GetDevices().FirstOrDefault(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"));
        }
        catch
        {
            device = null;
        }

        if (device is null || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
            new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

        // A folder of its own each run, clear of whatever earlier runs left
        var root = $"/sdcard/adbx-leftover-{Guid.NewGuid():N}";
        Shell(device, $"mkdir -p {root}/sub && echo a > {root}/a.txt && echo b > {root}/sub/b.txt");
        try
        {
            // An app still holding a file at unmount keeps the registration, for a later cleanup to finish
            var local = DeviceMount.MountAndMap(viewModel, root)!;
            var syncRootId = DeviceMount.Current!.SyncRootId;
            Assert.AreEqual("a", Cmd($"type \"{Path.Combine(local, "a.txt")}\""));
            Cmd($"dir /s /b \"{local}\"");
            using (new FileStream(Path.Combine(local, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
                DeviceMount.Unmount();

            Assert.IsTrue(IsRegistered(syncRootId), "unregistered with a placeholder left, which orphans it for good");
            DeviceMount.CleanUpStaleMounts();
            Assert.IsFalse(IsRegistered(syncRootId), "the cleanup should finish once the file is closed");
            Assert.IsFalse(Directory.Exists(local), $"{local} should be gone");

            // A crash: still registered, no longer connected
            local = DeviceMount.MountAndMap(viewModel, root)!;
            Cmd($"dir /s /b \"{local}\"");
            var mount = DeviceMount.Current!;
            var keyField = typeof(DeviceMount).GetField("_connectionKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Vanara.PInvoke.CldApi.CfDisconnectSyncRoot(((Vanara.PInvoke.CldApi.CF_CONNECTION_KEY?)keyField.GetValue(mount))!.Value);
            keyField.SetValue(mount, null);
            typeof(DeviceMount).GetProperty(nameof(DeviceMount.Current))!.SetValue(null, null);
            Assert.IsTrue(File.Exists(Path.Combine(local, "a.txt")), "the crash state wasn't reproduced");

            DeviceMount.CleanUpStaleMounts();
            Assert.IsFalse(IsRegistered(mount.SyncRootId));
            Assert.IsFalse(Directory.Exists(local), $"{local} should be gone after a crash");
        }
        finally
        {
            DeviceMount.Unmount();
            Shell(device, $"rm -rf {root}");
        }
    }

    private static bool IsRegistered(string syncRootId)
        => Windows.Storage.Provider.StorageProviderSyncRootManager.GetCurrentSyncRoots().Any(r => r.Id == syncRootId);

    private static bool WaitFor(Func<bool> condition, int seconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds)
                return false;

            System.Threading.Thread.Sleep(200);
        }

        return true;
    }

    private static int ExplorerWindowsUnder(string folder)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        var count = 0;
        foreach (var window in shell.Windows())
        {
            string? path = null;
            try
            {
                path = ((dynamic)window).Document.Folder.Self.Path;
            }
            catch (Exception)
            { }

            if (path?.StartsWith(folder, StringComparison.OrdinalIgnoreCase) == true)
                count++;
        }

        return count;
    }

    private static List<string> Lines(string output) => [.. output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];

    private static string Cmd(string command)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {command}")
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
