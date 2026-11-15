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
/// The Cloud Files mount: folders listed from the device, files pulled on open, deletes and renames carried out on it, cleanup on unmount.
/// Mounts each USB device in turn, under the user's profile.
/// </summary>
[TestClass]
public class DeviceMountTests
{
    // Every name the tests create is pseudo-accented, with an emoji, to cover non-ASCII names end to end
    private static string N(string name) => TestNames.Pseudo(name);

    private static readonly string Root = $"/sdcard/{N("adbx-mount-test")}";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);

        // Deletes and renames run as the app's operations: they need its queue, and a UI dispatcher to add to it on
        using var ready = new System.Threading.ManualResetEventSlim();
        var ui = new System.Threading.Thread(() =>
        {
            typeof(ADB_Explorer.App).GetProperty(nameof(ADB_Explorer.App.AppDispatcher))!.SetValue(null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        ui.SetApartmentState(System.Threading.ApartmentState.STA);
        ui.Start();
        ready.Wait();
        Data.FileOpQ ??= new();
    }

    [TestMethod]
    [Timeout(120_000)]
    public void Mount_ListsAndReads()
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
                Shell(device, $"rm -rf {Root}; mkdir -p {Root}/{N("sub")}/{N("deeper")} {Root}/{N(".hidden-dir")} && cd {Root} && head -c 3000000 /dev/urandom > {N("a.bin")} "
                    + $"&& echo hello > {N("sub")}/{N("b.txt")} && touch {N("sub")}/{LongName("0")} {N("sub")}/{LongName("z")} && echo 1 > {N("Case.txt")} && echo h > {N(".hidden.txt")}; echo x > '{N("bad:name.txt")}'");

                local = DeviceMount.MountAndMap(viewModel, Root);
                Assert.IsNotNull(local);
                notes.AppendLine($"{device.Model}: shell registration {DeviceMount.Current!.IsShellRegistered}, folder {local}");

                // The platform doesn't fetch for the provider's own process, so look through another one
                var names = Lines(Cmd($"dir /a /b \"{local}\""));
                CollectionAssert.Contains(names, N("a.bin"), device.Model);
                CollectionAssert.Contains(names, N("sub"));
                CollectionAssert.DoesNotContain(names, N("bad:name.txt"));
                CollectionAssert.DoesNotContain(names, FileNameEncoding.ToWindows(N("bad:name.txt")), "names aren't mapped on FUSE drives");
                Assert.AreEqual(1, names.Count(n => string.Equals(n, N("case.txt"), StringComparison.OrdinalIgnoreCase)), string.Join(", ", names));

                // Dot entries are there, but hidden from a plain listing
                CollectionAssert.IsSubsetOf(new[] { N(".hidden.txt"), N(".hidden-dir") }, names);
                var visible = Lines(Cmd($"dir /b \"{local}\""));
                CollectionAssert.DoesNotContain(visible, N(".hidden.txt"));
                CollectionAssert.DoesNotContain(visible, N(".hidden-dir"));
                CollectionAssert.Contains(visible, N("a.bin"));

                var mountRoot = DeviceMount.Current.LocalRoot;
                CollectionAssert.DoesNotContain(Lines(Cmd($"dir /a /b \"{mountRoot}\"")), AdbExplorerConst.RECYCLE_FOLDER, "the recycle bin is left out");

                Assert.AreEqual(3000000L, new FileInfo(Path.Combine(local, N("a.bin"))).Length);

                // Pulled on open, chunk by chunk
                var copy = Path.Combine(Path.GetTempPath(), $"adbx-mount-{Guid.NewGuid():N}.bin");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var copyOutput = Cmd($"copy /y \"{Path.Combine(local, N("a.bin"))}\" \"{copy}\"");
                Assert.IsTrue(File.Exists(copy), $"{device.Model}: {copyOutput}");
                notes.AppendLine($"  3 MB hydrated in {watch.ElapsedMilliseconds} ms");

                var localHash = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(copy)));
                File.Delete(copy);
                var deviceHash = Shell(device, $"md5sum {Root}/{N("a.bin")}").Split(' ')[0];
                Assert.AreEqual(deviceHash, localHash, device.Model);

                // Long names beside it once made every placeholder in the folder unreadable
                Assert.AreEqual("hello", ReadThroughShell(Path.Combine(local, N("sub"), N("b.txt"))));
                CollectionAssert.Contains(Lines(Cmd($"dir /b \"{Path.Combine(local, N("sub"))}\"")), N("deeper"));

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
        var root = $"/sdcard/{N("adbx-leftover")}-{Guid.NewGuid():N}";
        Shell(device, $"mkdir -p {root}/{N("sub")} && echo a > {root}/{N("a.txt")} && echo b > {root}/{N("sub")}/{N("b.txt")}");
        try
        {
            // An app still holding a file at unmount keeps the registration, for a later cleanup to finish
            var local = DeviceMount.MountAndMap(viewModel, root)!;
            var syncRootId = DeviceMount.Current!.SyncRootId;
            Assert.AreEqual("a", ReadThroughShell(Path.Combine(local, N("a.txt"))));
            Cmd($"dir /s /b \"{local}\"");
            using (new FileStream(Path.Combine(local, N("a.txt")), FileMode.Open, FileAccess.Read, FileShare.Read))
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
            Assert.IsTrue(File.Exists(Path.Combine(local, N("a.txt"))), "the crash state wasn't reproduced");

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

    [TestMethod]
    [Timeout(5_000)]
    public void Mount_ShowsDrivesAsFolders()
    {
        DeviceData? device = null;
        string? sdPath = null;
        try
        {
            // A device with an SD card: a storage volume besides the emulated one
            foreach (var candidate in new AdbClient().GetDevices().Where(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-")))
            {
                var volume = Lines(Shell(candidate, "ls -1 /storage")).FirstOrDefault(name => name is not ("emulated" or "self"));
                if (volume is not null)
                {
                    device = candidate;
                    sdPath = $"/storage/{volume}";
                    break;
                }
            }
        }
        catch
        { }

        if (device is null || sdPath is null || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device with an SD card is required.");

        var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
            new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));
        var sdDrive = new LogicalDriveViewModel(new LogicalDrive(path: sdPath));
        sdDrive.SetRemovableInfo(AbstractDrive.DriveType.Expansion, null, null);
        viewModel.Drives.Add(new LogicalDriveViewModel(new LogicalDrive(path: "/sdcard")));
        viewModel.Drives.Add(sdDrive);

        var internalName = AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Internal);
        var sdName = sdDrive.DisplayName;
        var folder = $"{sdPath}/{N("adbx-drives")}-{Guid.NewGuid():N}";
        Shell(device, $"mkdir -p {folder} && echo sd > {folder}/{N("a.txt")}");
        try
        {
            var local = DeviceMount.MountAndMap(viewModel, folder)!;
            var root = DeviceMount.Current!.LocalRoot;
            Assert.AreEqual(Path.Combine(root, sdName, FileHelper.GetFullName(folder)), local);

            // The root holds a folder per drive, each with the app's icon for it
            var rootNames = Lines(Cmd($"dir /b \"{root}\""));
            var tempName = AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Temp);
            CollectionAssert.AreEquivalent(new[] { internalName, sdName, tempName }, rootNames, $"{root}: {string.Join(" | ", rootNames)}");
            Assert.AreEqual("sd", ReadThroughShell(Path.Combine(local, N("a.txt"))));
            foreach (var drive in new[] { internalName, sdName })
            {
                var driveFolder = Path.Combine(root, drive);
                Assert.IsTrue(File.GetAttributes(driveFolder).HasFlag(FileAttributes.ReadOnly), $"{drive} should be read-only, for its desktop.ini to count");
                StringAssert.Contains(File.ReadAllText(Path.Combine(driveFolder, "desktop.ini")), "IconResource=", drive);
            }

            // Neither a drive's folder nor its icon file is the device's to change
            Cmd($"ren \"{Path.Combine(root, sdName)}\" {N("renamed")}");
            Assert.IsTrue(Directory.Exists(Path.Combine(root, sdName)), "a drive's folder was renamed");
            Cmd($"del /f /a:hs \"{Path.Combine(root, sdName, "desktop.ini")}\"");
            Assert.IsTrue(File.Exists(Path.Combine(root, sdName, "desktop.ini")), "a drive's icon file was deleted");

            DeviceMount.Unmount();
            Assert.IsFalse(Directory.Exists(root), $"{root} should be gone");
        }
        finally
        {
            DeviceMount.Unmount();
            Shell(device, $"rm -rf {folder}");
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public void Mount_ShowsTempDrive()
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

        var tempName = AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Temp);
        foreach (var device in devices)
        {
            var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
                new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));
            viewModel.Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));

            var folder = $"{AdbExplorerConst.TEMP_PATH}/{N("adbx-temp")}-{Guid.NewGuid():N}";
            Shell(device, $"mkdir -p {folder} && echo t > {folder}/{N("a.txt")}");
            try
            {
                var local = DeviceMount.MountAndMap(viewModel, folder)!;
                var root = DeviceMount.Current!.LocalRoot;
                Assert.AreEqual(Path.Combine(root, tempName, FileHelper.GetFullName(folder)), local, device.Model);
                CollectionAssert.Contains(Lines(Cmd($"dir /b \"{root}\"")), tempName, device.Model);
                Assert.AreEqual("t", ReadThroughShell(Path.Combine(local, N("a.txt"))), device.Model);
            }
            finally
            {
                DeviceMount.Unmount();
                Shell(device, $"rm -rf {folder}");
            }
        }
    }

    [TestMethod]
    [Timeout(15_000)]
    public void Mount_MapsNamesWindowsRefusesOffFuse()
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

        // The device names, by their content; the temp drive is ext4, which takes them all
        string[] deviceNames = ["a:b.txt", "what?.txt", "trail.", "trail ", "CON.txt", "Case.txt", "case.txt", "real：fw.txt", "c\u0001.txt"];
        foreach (var device in devices)
        {
            var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
                new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));
            viewModel.Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));

            var folder = $"{AdbExplorerConst.TEMP_PATH}/{N("adbx-names")}-{Guid.NewGuid():N}";
            Shell(device, $"""
                mkdir -p {folder} && cd {folder} && echo 0 > 'a:b.txt' && echo 1 > 'what?.txt' && echo 2 > 'trail.' && echo 3 > 'trail ' \
                && echo 4 > CON.txt && echo 5 > Case.txt && echo 6 > case.txt && echo 7 > 'real：fw.txt' && echo 8 > "c$(printf '\001').txt"
                """.ReplaceLineEndings("\n"));
            var substitute = Data.Settings.SubstituteIllegalNames;
            try
            {
                Assert.AreEqual(deviceNames.Length, Lines(Shell(device, $"ls -1 {folder}")).Count, $"{device.Model}: not every test file was created");

                // Without the setting, only the names Windows takes as they are show
                Data.Settings.SubstituteIllegalNames = false;
                var local = DeviceMount.MountAndMap(viewModel, folder)!;
                var plainNames = Lines(Cmd($"dir /b \"{local}\""));
                Assert.AreEqual(2, plainNames.Count, $"{device.Model}: {string.Join(" | ", plainNames)}");
                CollectionAssert.Contains(plainNames, "real：fw.txt", device.Model);
                DeviceMount.Unmount();

                Data.Settings.SubstituteIllegalNames = true;
                local = DeviceMount.MountAndMap(viewModel, folder)!;
                var windowsNames = Lines(Cmd($"dir /b \"{local}\""));

                // Every one shows, under a name that maps back to the device name, with the device file's content
                CollectionAssert.AreEquivalent(deviceNames, windowsNames.Select(FileNameEncoding.ToDevice).ToList(), $"{device.Model}: {string.Join(" | ", windowsNames)}");
                foreach (var windowsName in windowsNames)
                {
                    var index = Array.IndexOf(deviceNames, FileNameEncoding.ToDevice(windowsName));
                    Assert.AreEqual(index.ToString(), ReadThroughShell(Path.Combine(local, windowsName)), $"{device.Model}: {windowsName}");
                }

                // A name typed with a lookalike reaches the device as the character it stands for
                Cmd($"ren \"{Path.Combine(local, FileNameEncoding.ToWindows("a:b.txt")!)}\" \"x？y.txt\"");
                CollectionAssert.Contains(Lines(Shell(device, $"ls -1 {folder}")), "x?y.txt", device.Model);
            }
            finally
            {
                DeviceMount.Unmount();
                Data.Settings.SubstituteIllegalNames = substitute;
                Shell(device, $"rm -rf {folder}");
            }
        }
    }

    [TestMethod]
    [Timeout(25_000)]
    public void Mount_ListsLargeFoldersProgressively()
    {
        const int count = 3000;
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

            // Android/data skips the storage layer that makes each new file cost ~45 ms on Android 11+
            var root = $"/sdcard/Android/data/{N("adbx-many")}-{Guid.NewGuid():N}";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Shell(device, $"mkdir -p {root}/{N("big")} {root}/{N("big2")} && for d in {N("big")} {N("big2")}; do "
                + $"(cd {root}/$d && seq 1 {count} | sed 's/$/{N("-photo.jpg")}/' | xargs touch); done");
            notes.AppendLine($"{device.Model}: {count * 2} files created in {watch.ElapsedMilliseconds} ms");
            try
            {
                var local = DeviceMount.MountAndMap(viewModel, root)!;
                var folder = Path.Combine(local, N("big"));

                // The first listing returns with what arrived so far, and the rest follows
                watch.Restart();
                var first = Lines(Cmd($"dir /b \"{folder}\"")).Count;
                notes.AppendLine($"  first listing: {first} entries in {watch.ElapsedMilliseconds} ms");
                Assert.IsTrue(WaitFor(() => Lines(Cmd($"dir /b \"{folder}\"")).Count == count, 15), $"{device.Model}: not every entry arrived");
                notes.AppendLine($"  all {count} in {watch.ElapsedMilliseconds} ms");

                // Then it's complete, so it isn't listed from the device again: a file added there now stays unseen
                System.Threading.Thread.Sleep(500);
                Shell(device, $"touch {root}/{N("big")}/{N("late.jpg")}");
                CollectionAssert.DoesNotContain(Lines(Cmd($"dir /b \"{folder}\"")), N("late.jpg"), $"{device.Model}: listed again after completing");

                // Unmounting while a folder fills stops it, without hanging or leaving anything behind.
                // The test's own process sees placeholders on disk without asking for the folder to be listed.
                var folder2 = Path.Combine(local, N("big2"));
                var listing = System.Threading.Tasks.Task.Run(() => Cmd($"dir /b \"{folder2}\""));
                var filled = 0;
                watch.Restart();
                while (filled == 0 && watch.ElapsedMilliseconds < 10_000)
                {
                    if (Directory.Exists(folder2))
                        filled = Directory.EnumerateFileSystemEntries(folder2).Count();
                }

                Assert.AreNotEqual(0, filled, $"{device.Model}: {folder2} didn't start filling");
                watch.Restart();
                DeviceMount.Unmount();
                notes.AppendLine($"  {filled} of {count} in when unmounting");
                listing.Wait(5000);
                notes.AppendLine($"  unmounted mid-listing in {watch.ElapsedMilliseconds} ms");
                Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"{device.Model}: unmounting took {watch.ElapsedMilliseconds} ms");
                Assert.IsFalse(Directory.Exists(local), $"{device.Model}: {local} should be gone");
            }
            finally
            {
                DeviceMount.Unmount();
                Shell(device, $"rm -rf {root}");
            }
        }

        Console.WriteLine(notes);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Mount_DeletesAndRenamesOnDevice()
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

            var root = $"/sdcard/{N("adbx-change")}-{Guid.NewGuid():N}";
            Shell(device, $"mkdir -p {root}/{N("sub")}/{N("inner")} {root}/{N("folder")} && cd {root} && echo a > {N("a.txt")} && echo b > {N("b.txt")} && echo c > {N("c.txt")} "
                + $"&& echo m > {N("move.txt")} && echo i > {N("sub")}/{N("inner")}/{N("i.txt")} && echo f > {N("folder")}/{N("f.txt")} && echo k > {N("keep.txt")} && echo r > {N("recycle.txt")}");

            var trashExisted = Shell(device, $"[ -d {AdbExplorerConst.RECYCLE_PATH} ] && echo yes") == "yes";
            var trashBefore = TrashEntries(device);
            var outside = Path.Combine(Path.GetTempPath(), $"adbx-outside-{Guid.NewGuid():N}");
            try
            {
                var local = DeviceMount.MountAndMap(viewModel, root)!;
                Cmd($"dir /s /b \"{local}\"");
                // Names relative to the test folder, pseudo-accented segment by segment
                string Local(params string[] parts) => Path.Combine([local, .. parts.Select(N)]);
                bool OnDevice(string relative) => Shell(device, $"[ -e '{root}/{TestNames.PseudoPath(relative)}' ] && echo yes") == "yes";
                string Cat(string relative) => Shell(device, $"cat '{root}/{TestNames.PseudoPath(relative)}'");

                Data.Settings.EnableRecycle = false;
                var output = Cmd($"del /f \"{Local("a.txt")}\"");
                Assert.IsFalse(File.Exists(Local("a.txt")), output);
                Assert.IsFalse(OnDevice("a.txt"), $"{device.Model}: delete");

                output = Cmd($"ren \"{Local("b.txt")}\" {N("b2.txt")}");
                Assert.IsTrue(File.Exists(Local("b2.txt")), output);
                Assert.AreEqual("b", Cat("b2.txt"), $"{device.Model}: rename");
                Assert.IsFalse(OnDevice("b.txt"));

                // Storage that ignores case needs a temporary name in between
                output = Cmd($"ren \"{Local("c.txt")}\" {N("C.txt")}");
                var listed = Lines(Shell(device, $"ls -1 {root}"));
                CollectionAssert.Contains(listed, N("C.txt"), $"{device.Model}: case-only rename. {output}");
                CollectionAssert.DoesNotContain(listed, N("c.txt"));

                output = Cmd($"move \"{Local("move.txt")}\" \"{Local("sub")}\"");
                Assert.IsTrue(File.Exists(Local("sub", "move.txt")), output);
                Assert.AreEqual("m", Cat("sub/move.txt"), $"{device.Model}: move into a subfolder");
                Assert.IsFalse(OnDevice("move.txt"));

                output = Cmd($"ren \"{Local("folder")}\" {N("folder2")}");
                Assert.IsTrue(Directory.Exists(Local("folder2")), output);
                Assert.AreEqual("f", Cat("folder2/f.txt"), $"{device.Model}: folder rename");

                output = Cmd($"rmdir /s /q \"{Local("sub")}\"");
                Assert.IsFalse(Directory.Exists(Local("sub")), output);
                Assert.IsFalse(OnDevice("sub"), $"{device.Model}: folder delete");

                // Out of the mount isn't supported yet: refused, and nothing changes on either side
                Directory.CreateDirectory(outside);
                output = Cmd($"move \"{Local("keep.txt")}\" \"{outside}\"");
                Assert.IsTrue(File.Exists(Local("keep.txt")), output);
                Assert.IsTrue(OnDevice("keep.txt"), $"{device.Model}: the device must keep a file moved out");
                notes.AppendLine($"{device.Model} move out: {output}");

                // Into the app's recycle bin when it's on, indexed for restoring
                Data.Settings.EnableRecycle = true;
                output = Cmd($"del /f \"{Local("recycle.txt")}\"");
                Assert.IsFalse(File.Exists(Local("recycle.txt")), output);
                Assert.IsFalse(OnDevice("recycle.txt"), $"{device.Model}: recycle");
                // The index is written once the move has completed, just after Windows got its answer
                var indexes = "";
                WaitFor(() =>
                {
                    indexes = string.Join("\n", TrashEntries(device).Except(trashBefore)
                        .Where(e => e.EndsWith(AdbExplorerConst.RECYCLE_INDEX_SUFFIX))
                        .Select(e => Shell(device, $"cat '{AdbExplorerConst.RECYCLE_PATH}/{e}'")));
                    return indexes.Contains($"{root}/{N("recycle.txt")}");
                }, 3);
                StringAssert.Contains(indexes, $"{root}/{N("recycle.txt")}",
                    $"{device.Model}: the recycle bin index should point back to the file. New in the recycle bin: {string.Join(", ", TrashEntries(device).Except(trashBefore))}");
            }
            finally
            {
                Data.Settings.EnableRecycle = false;
                DeviceMount.Unmount();
                Shell(device, $"rm -rf {root}");
                foreach (var entry in TrashEntries(device).Except(trashBefore))
                    Shell(device, $"rm -rf '{AdbExplorerConst.RECYCLE_PATH}/{entry}'");

                if (!trashExisted)
                    Shell(device, $"rmdir {AdbExplorerConst.RECYCLE_PATH}");

                if (Directory.Exists(outside))
                    Directory.Delete(outside, true);
            }
        }

        Console.WriteLine(notes);
    }

    private static List<string> TrashEntries(DeviceData device)
        => Lines(Shell(device, $"ls -1a {AdbExplorerConst.RECYCLE_PATH} 2>/dev/null")).Where(e => e is not "." and not "..").ToList();

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

    /// <summary>Through cmd, as the platform doesn't fetch for the provider's own process. UTF-16 output (/u), for names outside the code page.</summary>
    private static string Cmd(string command)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/u /c {command}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return output.Trim();
    }

    /// <summary>A file's text, copied out through cmd: what type prints isn't reliably converted under /u.</summary>
    /// <summary>A file name well past the length that used to corrupt its folder, sorted by its first letter.</summary>
    private static string LongName(string first) => N($"{first}-{new string('l', 70)}.txt");

    private static string ReadThroughShell(string path)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"adbx-read-{Guid.NewGuid():N}");
        try
        {
            Cmd($"copy /y \"{path}\" \"{copy}\"");
            if (!File.Exists(copy))
                return "";

            return File.ReadAllText(copy).Trim();
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
