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
    // Pseudo-accented names, with an emoji, to cover non-ASCII names end to end
    private static string N(string name) => TestNames.Pseudo(name);

    private static readonly string Root = $"/sdcard/{N("adbx-sync-tests")}";
    private static readonly string A = N("a.bin");
    private static readonly string Sub = N("sub");
    private static readonly string B = N("b.bin");
    private static readonly string Deeper = N("deeper");
    private static readonly string Zero = N("zero.bin");
    private static readonly string Missing = N("missing");

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
        => Shell(device, $"rm -rf {Root}; mkdir -p {Root}/{Sub}/{Deeper} && cd {Root} && head -c 4321 /dev/urandom > {A} "
            + $"&& head -c 99 /dev/urandom > {Sub}/{B} && : > {Sub}/{Deeper}/{Zero} && touch -t 200001020304.05 {A} "
            // A distinct access time, so a listing that mixes it up with the modified time fails
            + $"&& touch -a -t 199901020304.05 {A}");

    [TestMethod]
    public void ListDirectoryEntries_GivesTypesSizesAndTimes()
    {
        foreach (var device in Devices())
        {
            try
            {
                Setup(device);

                var entries = AdbService.ListDirectoryEntries(device.Serial, Root, CancellationToken.None).ToDictionary(e => e.FullName);

                CollectionAssert.AreEquivalent(new[] { A, Sub }, entries.Keys.ToList(), device.Model);
                Assert.AreEqual(FileType.File, entries[A].Type);
                Assert.AreEqual(4321L, entries[A].Size);
                Assert.AreEqual($"{Root}/{A}", entries[A].FullPath);
                Assert.AreEqual(long.Parse(Shell(device, $"stat -c %Y {Root}/{A}")), new DateTimeOffset(entries[A].ModifiedTime!.Value).ToUnixTimeSeconds());
                Assert.AreEqual(FileType.Folder, entries[Sub].Type);
                Assert.IsNull(entries[Sub].Size);
            }
            finally
            {
                Shell(device, $"rm -rf {Root}");
            }
        }
    }

    [TestMethod]
    public void ListDirectoryEntries_Lis2GivesAccessTimeAndOwnerIds()
    {
        foreach (var device in Devices())
        {
            try
            {
                Setup(device);

                var entries = AdbService.ListDirectoryEntries(device.Serial, Root, CancellationToken.None).ToDictionary(e => e.FullName);

                // LIST v1 has no access time or owner, so the selection stat still fills them there
                if (!new AdbClient().GetFeatureSet(device).Contains(AdbService.FEATURE_LS_V2))
                {
                    Assert.IsNull(entries[A].AccessTime, device.Model);
                    continue;
                }

                foreach (var name in new[] { A, Sub })
                {
                    var stat = Shell(device, $"stat -c '%X %u %g %Z' {Root}/{name}").Split(' ');

                    Assert.AreEqual(long.Parse(stat[0]), entries[name].AccessTime?.ToUnixTimeSeconds(), $"{device.Model} {name}");
                    Assert.AreEqual(int.Parse(stat[1]), entries[name].OwnerUid, $"{device.Model} {name}");
                    Assert.AreEqual(int.Parse(stat[2]), entries[name].OwnerGid, $"{device.Model} {name}");
                    Assert.AreEqual(long.Parse(stat[3]), entries[name].ChangeTime?.ToUnixTimeSeconds(), $"{device.Model} {name}");
                }

                // Listed times are shown in the device's zone, as stat prints them
                var zoneId = Shell(device, $"getprop {AdbService.TIME_ZONE_PROP}");
                Assert.IsTrue(TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out _), $"{device.Model} {zoneId}");
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

                Assert.AreEqual(4321L, AdbService.TryGetFileSize(device.Serial, $"{Root}/{A}", CancellationToken.None), device.Model);
                Assert.IsNull(AdbService.TryGetFileSize(device.Serial, $"{Root}/{Missing}", CancellationToken.None));
                Assert.AreEqual(DevicePathKind.Directory, AdbService.TryGetPathKind(device.Serial, $"{Root}/{Sub}"));
                Assert.AreEqual(DevicePathKind.RegularFile, AdbService.TryGetPathKind(device.Serial, $"{Root}/{A}"));
                Assert.IsNull(AdbService.TryGetPathKind(device.Serial, $"{Root}/{Missing}"));

                // A /system/bin applet is a symlink to toybox, which lstat reports as the link
                var lstat = AdbService.SyncLstat(device.Serial, "/system/bin/ls");
                Assert.AreEqual(0xA000u, (uint)lstat.Mode & AdbService.S_IFMT, device.Model);
                Assert.AreEqual(DevicePathKind.Unknown, AdbService.TryGetPathKind(device.Serial, "/system/bin/ls"));

                var shellSize = Shell(device, $"stat -c %s {Root}/{Sub}/{B}");
                Assert.AreEqual(long.Parse(shellSize), AdbService.TryGetFileStat(device.Serial, $"{Root}/{Sub}/{B}")?.Size);
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
                    [$"{Root}/{A}"] = 4321,
                    [$"{Root}/{Sub}"] = null,
                    [$"{Root}/{Sub}/{B}"] = 99,
                    [$"{Root}/{Sub}/{Deeper}"] = null,
                    [$"{Root}/{Sub}/{Deeper}/{Zero}"] = 0,
                };

                var tree = FileHelper.GetFolderTree([Root], deviceId: device.Serial).ToDictionary(t => t.Name.Replace("//", "/"));
                CollectionAssert.AreEquivalent(expected.ToList(), tree.ToDictionary(p => p.Key, p => p.Value.Size).ToList(), device.Model);
                var aTime = long.Parse(Shell(device, $"stat -c %Y {Root}/{A}"));
                Assert.AreEqual((double)aTime, Math.Floor(tree[$"{Root}/{A}"].Date!.Value));

                // Files only: the given paths themselves, and a folder's contents
                var files = FileHelper.GetFolderTree([$"{Root}/{A}"], isFolder: false, deviceId: device.Serial);
                Assert.AreEqual(1, files.Length);
                Assert.AreEqual(4321L, files[0].Size);

                var newer = FileHelper.GetFolderTree([Root], out _, deviceId: device.Serial, modifiedAfter: aTime + 1);
                Assert.IsFalse(newer.Any(t => t.Name.EndsWith(A)), device.Model);
                Assert.IsTrue(newer.Any(t => t.Name.EndsWith(B)), device.Model);
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
