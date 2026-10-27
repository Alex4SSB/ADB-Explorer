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
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Test;

/// <summary>
/// Virtual Windows sources (zip members, FileContents streams) pushed to a device without the temp folder.
/// Run: dotnet test --filter VirtualPush
/// </summary>
[TestClass]
[DoNotParallelize]
public class VirtualPushTests
{
    private const string RemoteDir = "/sdcard/adb-explorer-test-virtual";

    private static DeviceData? _device;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);

        try
        {
            _device = new AdbClient().GetDevices()
                .FirstOrDefault(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"));
        }
        catch
        {
            _device = null;
        }
    }

    [TestMethod]
    public void VirtualPush_PushesZipMembers()
    {
        var device = RequireDevice();
        var local = NewLocalDir();
        var zip = MakeZip(local);
        Shell(device, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}");

        try
        {
            RunSta(() =>
            {
                using var folder = ShellItem.Open(Path.Combine(zip, "folder"));
                using var file = ShellItem.Open(Path.Combine(zip, "top.bin"));
                List<SyncFile> items = [new(folder, includeContent: true), new(file)];

                Assert.IsTrue(items.All(i => i.IsVirtual), "Zip members aren't on the file system");
                Assert.IsNotNull(items[1].OpenContent);

                var op = FileSyncOperation.SyncBatch(FileOperation.OperationType.Push, items, new(RemoteDir, FileType.Folder), ViewModel(device), Dispatcher.CurrentDispatcher);
                Assert.AreNotEqual("Windows", op.SourcePathString, "A zip member's source is the zip");
                Run(op);
            });

            AssertSameContent(device, Path.Combine(local, "content"), RemoteDir);
        }
        finally
        {
            Shell(device, $"rm -rf {RemoteDir}");
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void VirtualPush_ConflictsInsideZipFolderAreFound()
    {
        var device = RequireDevice();
        var local = NewLocalDir();
        var zip = MakeZip(local);

        // The device already has the folder, with a different sub/a.bin
        Shell(device, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}/folder/sub && echo different > {RemoteDir}/folder/sub/a.bin");

        try
        {
            IReadOnlyList<FileMergeHelper.ConflictComparisonInfo> conflicts = [];
            RunSta(() =>
            {
                var candidate = new FileMergeHelper.ConflictCandidate(Path.Combine(zip, "folder"), "folder", true, null, null);
                var listing = FileMergeHelper.TryListAndroidDirByName(device.Serial, RemoteDir, StringComparer.InvariantCulture);
                conflicts = FileMergeHelper.ExpandConflicts(
                    [candidate],
                    RemoteDir,
                    name => FileMergeHelper.GetAndroidDestEntry(RemoteDir, name, listing),
                    targetIsWindows: false,
                    device.Serial,
                    StringComparer.InvariantCulture);
            });

            CollectionAssert.AreEqual(new[] { "folder/sub/a.bin" }, conflicts.Select(c => c.Name).ToArray());
        }
        finally
        {
            Shell(device, $"rm -rf {RemoteDir}");
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void VirtualPush_PushesFileContents()
    {
        var device = RequireDevice();
        Shell(device, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}");

        // dir\nested.bin comes before its folder, and orphan\file.bin has no folder entry at all
        Dictionary<string, byte[]> files = new()
        {
            [@"dir\nested.bin"] = RandomNumberGenerator.GetBytes(700_001),
            [@"top.bin"] = RandomNumberGenerator.GetBytes(3),
            [@"orphan\file.bin"] = RandomNumberGenerator.GetBytes(65_536 * 3 + 5),
        };

        List<FileDescriptor> descriptors = [];
        foreach (var (name, content) in files)
        {
            descriptors.Add(new()
            {
                Name = name,
                Length = content.Length,
                ChangeTimeUtc = new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc),
                Stream = () => StreamOf(content),
            });
        }

        descriptors.Add(new() { Name = "dir", IsDirectory = true });

        try
        {
            RunSta(() =>
            {
                var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
                vfdo.SetFileDescriptors(descriptors);

                var roots = CopyPasteService.GetContentTrees(vfdo, [.. descriptors]);
                CollectionAssert.AreEquivalent(new[] { "dir", "top.bin", "orphan" }, roots.Select(r => r.FullName).ToArray());

                var op = FileSyncOperation.SyncBatch(FileOperation.OperationType.Push, roots, new(RemoteDir, FileType.Folder), ViewModel(device), Dispatcher.CurrentDispatcher);
                
                // Another app's files have no folder to show as the source
                Assert.AreEqual("Windows", op.SourcePathString);
                Assert.IsFalse(op.IsSourceNavigable);
                Run(op);
            });

            foreach (var (name, content) in files)
            {
                var remote = $"{RemoteDir}/{name.Replace('\\', '/')}";
                Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(content)), Shell(device, $"sha256sum {remote}").Split(' ')[0], name);
            }

            Assert.AreEqual(new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), Shell(device, $"stat -c %Y {RemoteDir}/top.bin"));
        }
        finally
        {
            Shell(device, $"rm -rf {RemoteDir}");
        }
    }

    [TestMethod]
    public void VirtualPush_ContentTreesSkipNamesSteppingUpAndStayApart()
    {
        RunSta(() =>
        {
            var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
            FileDescriptor[] descriptors =
            [
                new() { Name = @"..\escape.bin", Length = 1 },
                new() { Name = "../escape.bin", Length = 1 },
                new() { Name = "kept.bin", Length = 1 },
            ];

            var first = CopyPasteService.GetContentTrees(vfdo, descriptors);
            CollectionAssert.AreEqual(new[] { "kept.bin" }, first.Select(r => r.FullName).ToArray());

            // Two pastes of the same names are registered side by side while their conflicts are checked
            var second = CopyPasteService.GetContentTrees(vfdo, descriptors);
            Assert.AreNotEqual(first[0].FullPath, second[0].FullPath, true);
        });
    }

    /// <summary>A FileContents stream over <paramref name="content"/>, served the way ADB Explorer serves a device file.</summary>
    private static nint StreamOf(byte[] content)
    {
        var pipe = new SyncPipe(content.Length);
        var writer = new Thread(() =>
        {
            pipe.Write(content);
            pipe.Complete();
        });
        writer.Start();

        return new DevicePullStream(pipe, content.Length).ToComPointer();
    }

    /// <summary>A zip of folder/sub/a.bin, folder/b.bin and top.bin; the same files are left unzipped under content.</summary>
    private static string MakeZip(string local)
    {
        var content = Path.Combine(local, "content");
        Directory.CreateDirectory(Path.Combine(content, "folder", "sub"));
        File.WriteAllBytes(Path.Combine(content, "folder", "sub", "a.bin"), RandomNumberGenerator.GetBytes(1_048_583));
        File.WriteAllBytes(Path.Combine(content, "folder", "b.bin"), RandomNumberGenerator.GetBytes(4_099));
        File.WriteAllBytes(Path.Combine(content, "top.bin"), RandomNumberGenerator.GetBytes(300_007));

        var zip = Path.Combine(local, "archive.zip");
        ZipFile.CreateFromDirectory(content, zip);

        return zip;
    }

    private static void AssertSameContent(DeviceData device, string localRoot, string remoteRoot)
    {
        var localFiles = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).ToList();
        Assert.HasCount(3, localFiles);

        foreach (var file in localFiles)
        {
            var remote = $"{remoteRoot}/{Path.GetRelativePath(localRoot, file).Replace('\\', '/')}";
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), Shell(device, $"sha256sum {remote} 2>/dev/null").Split(' ')[0], remote);
        }
    }

    /// <summary>Starts <paramref name="op"/> as the queue would, pumps until it's over, and checks every file made it.</summary>
    private static void Run(FileOperation op)
    {
        op.Start();

        var sw = Stopwatch.StartNew();
        while (op.Status is FileOperation.OperationStatus.InProgress && sw.Elapsed < TimeSpan.FromMinutes(3))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(50);
        }

        // A file that failed still leaves the op Completed, counted as skipped
        Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
        Assert.IsEmpty(((AbstractSyncFileOperation)op).FailedFiles);
    }

    private static DeviceData RequireDevice()
    {
        if (_device is null || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        return _device!;
    }

    private static string NewLocalDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"adb-explorer-test-virtual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        return dir;
    }

    private static LogicalDeviceViewModel ViewModel(DeviceData device)
        => new(LogicalDevice.From(new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            OleInitialize(0);
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            throw new AggregateException(error);
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);
}
