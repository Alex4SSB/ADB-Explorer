#nullable enable

using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADB_Test;

/// <summary>
/// tar -cv member sizes collected through find list every member, links included, on every USB device.
/// </summary>
[TestClass]
public class ArchiveMemberBytesTests
{
    // Pseudo-accented names, with an emoji, to cover non-ASCII names end to end
    private static string N(string name) => TestNames.Pseudo(name);

    private static string P(string relativePath) => TestNames.PseudoPath(relativePath);

    private static readonly string Parent = $"/data/local/tmp/{N("adbx-member-bytes")}";
    private static readonly string Root = $"{Parent}/{N("src")}";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);
    }

    [TestMethod]
    public void CollectCreateMemberBytes_ListsAllMembers()
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

        foreach (var device in devices)
        {
            // Probes find -printf support, as on connection: older toybox (Android 9) lacks it
            ShellCommands.FindCommands(device.Serial);

            try
            {
                Shell(device, $"rm -rf {Parent}; mkdir -p {Root}/{P("sub/deeper")} {Root}/{N("empty")} && cd {Root} "
                    + $"&& head -c 12345 /dev/urandom > {N("a.bin")} && head -c 777 /dev/urandom > {P("sub/b.bin")} "
                    + $"&& : > {P("sub/deeper/zero.bin")} && ln -s {N("a.bin")} {N("link.bin")} && ln -s {N("sub")} {N("dirlink")}");

                var actual = ArchiveExtract.CollectCreateMemberBytes(device.Serial, [Root], CancellationToken.None);
                var message = $"{device.Model}: {Describe(actual)}";

                // Without find -printf the tree comes from sync listings, and adbd can't stat symlinks under /data/local/tmp
                IEnumerable<string> expectedKeys = Expected.Keys;
                if (ShellCommands.FindPrintf(device.Serial))
                    expectedKeys = expectedKeys.Concat(Links);

                CollectionAssert.AreEquivalent(expectedKeys.ToList(), actual.Keys.ToList(), message);
                foreach (var (key, size) in Expected)
                    Assert.AreEqual(size, actual[key], message);
            }
            finally
            {
                Shell(device, $"rm -rf {Parent}");
            }
        }
    }

    private static readonly Dictionary<string, long> Expected = new()
    {
        [P("src")] = 0,
        [P("src/a.bin")] = 12345,
        [P("src/empty")] = 0,
        [P("src/sub")] = 0,
        [P("src/sub/b.bin")] = 777,
        [P("src/sub/deeper")] = 0,
        [P("src/sub/deeper/zero.bin")] = 0,
    };

    // tar -v lists links as members
    private static readonly string[] Links = [P("src/link.bin"), P("src/dirlink")];

    private static string Describe(Dictionary<string, long> members)
        => string.Join(", ", members.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
