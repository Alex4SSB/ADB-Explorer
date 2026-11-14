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
using System.Linq;
using System.Text;
using System.Threading;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Test;

/// <summary>
/// Listing, stat and folder trees over the sync protocol (LIS2/LST2/STA2, or LIST/STAT v1), on every USB device.
/// </summary>
[TestClass]
public class SyncProtocolTests
{
    private const string Root = "/sdcard/adbx-sync-tests";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false, KeepDateModified = true };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);
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

    private static void Setup(DeviceData device)
        => Shell(device, $"rm -rf {Root}; mkdir -p {Root}/sub/deeper && cd {Root} && head -c 4321 /dev/urandom > a.bin "
            + "&& head -c 99 /dev/urandom > sub/b.bin && : > sub/deeper/zero.bin && touch -t 200001020304.05 a.bin");

    [TestMethod]
    public void ListDirectoryEntries_GivesTypesSizesAndTimes()
    {
        foreach (var device in Devices())
        {
            try
            {
                Setup(device);

                var entries = AdbService.ListDirectoryEntries(device.Serial, Root, CancellationToken.None).ToDictionary(e => e.FullName);

                CollectionAssert.AreEquivalent(new[] { "a.bin", "sub" }, entries.Keys.ToList(), device.Model);
                Assert.AreEqual(FileType.File, entries["a.bin"].Type);
                Assert.AreEqual(4321L, entries["a.bin"].Size);
                Assert.AreEqual($"{Root}/a.bin", entries["a.bin"].FullPath);
                Assert.AreEqual(long.Parse(Shell(device, $"stat -c %Y {Root}/a.bin")), new DateTimeOffset(entries["a.bin"].ModifiedTime!.Value).ToUnixTimeSeconds());
                Assert.AreEqual(FileType.Folder, entries["sub"].Type);
                Assert.IsNull(entries["sub"].Size);
            }
            finally
            {
                Shell(device, $"rm -rf {Root}");
            }
        }
    }

    [TestMethod]
    public void SingleStats_MatchShellStat()
    {
        foreach (var device in Devices())
        {
            try
            {
                Setup(device);

                Assert.AreEqual(4321L, AdbService.TryGetFileSize(device.Serial, $"{Root}/a.bin", CancellationToken.None), device.Model);
                Assert.IsNull(AdbService.TryGetFileSize(device.Serial, $"{Root}/missing", CancellationToken.None));
                Assert.AreEqual(DevicePathKind.Directory, AdbService.TryGetPathKind(device.Serial, $"{Root}/sub"));
                Assert.AreEqual(DevicePathKind.RegularFile, AdbService.TryGetPathKind(device.Serial, $"{Root}/a.bin"));
                Assert.IsNull(AdbService.TryGetPathKind(device.Serial, $"{Root}/missing"));

                // A /system/bin applet is a symlink to toybox, which lstat reports as the link
                var lstat = AdbService.SyncLstat(device.Serial, "/system/bin/ls");
                Assert.AreEqual(0xA000u, (uint)lstat.Mode & AdbService.S_IFMT, device.Model);
                Assert.AreEqual(DevicePathKind.Unknown, AdbService.TryGetPathKind(device.Serial, "/system/bin/ls"));

                var shellSize = Shell(device, $"stat -c %s {Root}/sub/b.bin");
                Assert.AreEqual(long.Parse(shellSize), AdbService.TryGetFileStat(device.Serial, $"{Root}/sub/b.bin")?.Size);
            }
            finally
            {
                Shell(device, $"rm -rf {Root}");
            }
        }
    }

    [TestMethod]
    public void GetFolderTree_SameWithFindOrListing()
    {
        foreach (var device in Devices())
        {
            ShellCommands.FindCommands(device.Serial);

            try
            {
                Setup(device);

                var expected = new Dictionary<string, long?>
                {
                    [$"{Root}/a.bin"] = 4321,
                    [$"{Root}/sub"] = null,
                    [$"{Root}/sub/b.bin"] = 99,
                    [$"{Root}/sub/deeper"] = null,
                    [$"{Root}/sub/deeper/zero.bin"] = 0,
                };

                var tree = FileHelper.GetFolderTree([Root], deviceId: device.Serial).ToDictionary(t => t.Name.Replace("//", "/"));
                CollectionAssert.AreEquivalent(expected.ToList(), tree.ToDictionary(p => p.Key, p => p.Value.Size).ToList(), device.Model);
                var aTime = long.Parse(Shell(device, $"stat -c %Y {Root}/a.bin"));
                Assert.AreEqual((double)aTime, Math.Floor(tree[$"{Root}/a.bin"].Date!.Value));

                // Files only: the given paths themselves, and a folder's contents
                var files = FileHelper.GetFolderTree([$"{Root}/a.bin"], isFolder: false, deviceId: device.Serial);
                Assert.AreEqual(1, files.Length);
                Assert.AreEqual(4321L, files[0].Size);

                var newer = FileHelper.GetFolderTree([Root], out _, deviceId: device.Serial, modifiedAfter: aTime + 1);
                Assert.IsFalse(newer.Any(t => t.Name.EndsWith("a.bin")), device.Model);
                Assert.IsTrue(newer.Any(t => t.Name.EndsWith("b.bin")), device.Model);
            }
            finally
            {
                Shell(device, $"rm -rf {Root}");
            }
        }
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
