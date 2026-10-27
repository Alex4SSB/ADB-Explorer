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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using static ADB_Explorer.Models.AbstractFile;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Test;

/// <summary>
/// A device folder copied by the Windows shell (the copy engine File Explorer pastes and drops with)
/// from our <see cref="VirtualFileDataObject"/>, its files pulled straight into the shell's streams.
/// Run: dotnet test --filter ShellStream
/// </summary>
[TestClass]
[DoNotParallelize]
public partial class ShellStreamTests
{
    private const string RemoteParent = "/sdcard/adb-explorer-test-shell";
    private const string RemoteRoot = $"{RemoteParent}/folder";

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
    public void ShellStream_ShellCopiesDeviceFolder()
    {
        if (_device is null || string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("An online adb device is required.");

        var device = _device!;
        Shell(device, $"rm -rf {RemoteParent}; mkdir -p {RemoteRoot}/sub/deeper && cd {RemoteRoot} "
            + "&& head -c 41943049 /dev/urandom > big.bin "
            + "&& for i in 1 2 3 4 5 6 7 8 9 10 11 12; do head -c $((i*7919)) /dev/urandom > sub/s$i.bin; done "
            + "&& echo hello > sub/deeper/tiny.txt && : > sub/deeper/empty.bin "
            + $"&& head -c 123457 /dev/urandom > {RemoteParent}/loose.bin");

        // The second copy is like pasting the clipboard again, after the op is over
        string[] localRoots = [.. Enumerable.Range(0, 2).Select(_ => Path.Combine(Path.GetTempPath(), $"adb-explorer-test-shell-{Guid.NewGuid():N}"))];
        foreach (var root in localRoots)
            Directory.CreateDirectory(root);

        try
        {
            FileSyncOperation op = null!;
            RunSta(() => op = CopyWithShell(device, localRoots));

            Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status);
            Assert.IsEmpty(op.FailedFiles);
            Assert.IsFalse(op.ValidationAllowed, "There are no files of ours to validate");

            var deviceHashes = Shell(device, $"cd {RemoteParent} && find folder loose.bin -type f | xargs sha256sum")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split("  "))
                .ToDictionary(parts => parts[1].Trim(), parts => parts[0]);

            Assert.HasCount(FileCount, deviceHashes);
            foreach (var root in localRoots)
            {
                foreach (var (path, hash) in deviceHashes)
                {
                    var local = Path.Combine(root, path.Replace('/', '\\'));
                    Assert.IsTrue(File.Exists(local), local);
                    Assert.AreEqual(hash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(local))), local);
                }
            }
        }
        finally
        {
            Shell(device, $"rm -rf {RemoteParent}");
            foreach (var root in localRoots)
                Directory.Delete(root, true);
        }
    }

    private const int FileCount = 16;

    /// <summary>What dragging a folder and the file next to it out of ADB Explorer does, but with the shell's drop target called directly.</summary>
    private static FileSyncOperation CopyWithShell(DeviceData device, string[] localRoots)
    {
        var viewModel = new LogicalDeviceViewModel(LogicalDevice.From(
            new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

        var tree = FileHelper.GetFolderTree([RemoteRoot], deviceId: device.Serial);
        FolderTree[] folderItems = [new(RemoteRoot, null, null), .. tree];

        var loosePath = $"{RemoteParent}/loose.bin";
        FolderTree loose = new(loosePath, long.Parse(Shell(device, $"stat -c %s {loosePath}")), null);

        var folderClass = new FileClass("folder", RemoteRoot, FileType.Folder);
        var looseClass = new FileClass("loose.bin", loosePath, FileType.File, size: loose.Size);
        List<FileClass.ShellPullItem> pullItems =
        [
            new(folderClass, new SyncFile(folderClass, tree), [.. folderItems.Where(i => !i.IsFolder).Select(i => i.Name)], null, null, null),
            new(looseClass, new SyncFile(looseClass), [loosePath], null, null, null),
        ];

        var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);

        // Items sharing a parent go in one op, which doesn't pretend to have a folder of ours as its target
        var ops = VirtualFileDataObject.CreateShellPulls(vfdo, pullItems, viewModel, Dispatcher.CurrentDispatcher);
        Assert.HasCount(1, ops);
        var op = ops[0];
        Assert.IsTrue(op.IsBatch);
        Assert.AreEqual("Windows", op.TargetPathString);
        Assert.IsFalse(op.IsTargetNavigable);

        FolderTree[] items = [.. folderItems, loose];
        vfdo.SetFileDescriptors(items.Select(item => new FileDescriptor
        {
            Name = FileHelper.ExtractRelativePath(item.Name, RemoteParent).Replace('/', '\\'),
            IsDirectory = item.IsFolder,
            Length = item.Size,
            ChangeTimeUtc = item.Date.FromUnixTime(),
            Stream = () => item.IsFolder ? 0 : op.OpenShellStream(item.Name, item.Size),
        }).ToList());

        // As the queue would
        op.Start();

        foreach (var localRoot in localRoots)
        {
            var dropTarget = GetDropTarget(localRoot);
            uint effect = 1; // DROPEFFECT_COPY
            dropTarget.DragEnter(vfdo, 1, default, ref effect);
            effect = 1;
            dropTarget.Drop(vfdo, 0, default, ref effect);

            // The shell copies on its own thread; pump until the op is over and the shell let go of the files
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMinutes(3)
                && (op.Status is FileOperation.OperationStatus.InProgress || !IsCopied(localRoot)))
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
                Thread.Sleep(50);
            }
        }

        return op;
    }

    private static bool IsCopied(string root)
    {
        try
        {
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
            foreach (var file in files)
            {
                using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            }

            return files.Count == FileCount;
        }
        catch (IOException)
        {
            return false;
        }
    }

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

    private static IDropTarget GetDropTarget(string folder)
    {
        var itemId = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(folder, 0, ref itemId, out var item);

        var bhid = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"); // BHID_SFUIObject
        var targetId = typeof(IDropTarget).GUID;
        item.BindToHandler(0, ref bhid, ref targetId, out var target);

        return (IDropTarget)Marshal.GetObjectForIUnknown(target);
    }

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, nint pbc, ref Guid riid, out IShellItem ppv);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int X;
        public int Y;
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        void DragEnter(ComDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
        void DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
        void DragLeave();
        void Drop(ComDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }
}
