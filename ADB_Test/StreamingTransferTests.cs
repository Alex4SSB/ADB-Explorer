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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Test;

/// <summary>
/// <see cref="SyncPipe"/>, and transfers that stream through it instead of a temp file.
/// The device tests need two devices (e.g. a phone and an emulator). Run: dotnet test --filter StreamingTransfer
/// </summary>
[TestClass]
[DoNotParallelize]
public class StreamingTransferTests
{
    private const string RemoteDir = "/sdcard/adb-explorer-test-stream";

    private static DeviceData[] _devices = [];

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };

        // As at app startup, so the ops' own shell commands (mkdir, rm) reach adb
        AdbService.VerifyAdbVersion(AdbExplorerConst.ADB_PROCESS);

        // A device on both USB and Wi-Fi is listed twice; its mDNS entry is skipped
        try
        {
            _devices = [.. new AdbClient().GetDevices()
                .Where(d => d.State is DeviceState.Online && !d.Serial.Contains("._adb-tls-"))
                .Take(2)];
        }
        catch
        {
            _devices = [];
        }
    }

    [TestMethod]
    public void SyncPipe_ReadFillsBufferAcrossSmallWrites()
    {
        var pipe = new SyncPipe();
        var writer = Task.Run(() =>
        {
            for (byte i = 0; i < 30; i++)
                pipe.Write([i]);

            pipe.Complete();
        });

        var buffer = new byte[20];
        Assert.AreEqual(20, pipe.Read(buffer));
        CollectionAssert.AreEqual(Enumerable.Range(0, 20).Select(i => (byte)i).ToArray(), buffer);

        Assert.AreEqual(10, pipe.Read(buffer));
        Assert.AreEqual(0, pipe.Read(buffer));
        writer.Wait();
        Assert.AreEqual(30, pipe.BytesRead);
    }

    [TestMethod]
    public void SyncPipe_FullPipeHoldsWriterUntilRead()
    {
        var pipe = new SyncPipe(sizeHint: 4);
        var writer = Task.Run(() => pipe.Write([1, 2, 3, 4, 5, 6]));

        Assert.IsFalse(writer.Wait(200), "The writer should wait for room");

        var buffer = new byte[6];
        var reader = Task.Run(() => pipe.Read(buffer));
        Assert.IsTrue(writer.Wait(2000));

        pipe.Complete();
        Assert.AreEqual(6, reader.Result);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, buffer);
    }

    [TestMethod]
    public void SyncPipe_HoldsAWholeOddSizedFile()
    {
        // Its own memory is two halves, the second a byte smaller
        var pipe = new SyncPipe(sizeHint: 5);
        Assert.IsTrue(Task.Run(() => pipe.Write([1, 2, 3, 4, 5])).Wait(2000), "A file within the pipe's size fits without a reader");
        pipe.Complete();

        var buffer = new byte[5];
        Assert.AreEqual(5, pipe.Read(buffer));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, buffer);
    }

    [TestMethod]
    public void SyncPipe_SourceErrorFailsReader()
    {
        var pipe = new SyncPipe();
        pipe.Write([1, 2, 3]);
        pipe.Complete(new InvalidOperationException("source lost"));

        var error = Assert.ThrowsExactly<IOException>(() => pipe.Read(new byte[3]));
        Assert.AreEqual("source lost", error.Message);
    }

    [TestMethod]
    public void SyncPipe_ClosedReaderFailsWaitingWriter()
    {
        var pipe = new SyncPipe(sizeHint: 2);
        var writer = Task.Run(() => pipe.Write([1, 2, 3, 4]));
        Assert.IsFalse(writer.Wait(200));

        pipe.CloseReader();
        var error = Assert.ThrowsExactly<AggregateException>(() => writer.Wait(2000));
        Assert.IsInstanceOfType<IOException>(error.InnerException);
    }

    [TestMethod]
    public void DevicePullStream_ReleasedUnreadClosesItsPipe()
    {
        var pipe = new SyncPipe(sizeHint: 2);
        var writer = Task.Run(() => pipe.Write([1, 2, 3, 4]));
        Assert.IsFalse(writer.Wait(200));

        // The shell lets go of the stream without reading it to the end
        Marshal.Release(OpenStream(pipe));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var error = Assert.ThrowsExactly<AggregateException>(() => writer.Wait(2000));
        Assert.IsInstanceOfType<IOException>(error.InnerException);
    }

    // Not inlined, so nothing but the COM reference holds the stream
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint OpenStream(SyncPipe pipe) => new DevicePullStream(pipe, 4).ToComPointer();

    [TestMethod]
    public void SyncPipe_BorrowsMemoryAheadOfReaderAndReturnsIt()
    {
        Data.Settings.TransferMemoryMB = 8;

        try
        {
            var data = new byte[3 * 1024 * 1024 + 7];
            Random.Shared.NextBytes(data);

            // All of it fits in the pipe's own buffer plus borrowed memory, so the writer never waits
            var pipe = new SyncPipe(data.Length, canBorrow: true);
            Assert.IsTrue(Task.Run(() => pipe.Write(data)).Wait(2000), "The writer shouldn't wait for the reader");
            pipe.Complete();
            Assert.IsGreaterThan(0L, TransferMemory.Reserved);

            var read = new byte[data.Length];
            Assert.AreEqual(data.Length, pipe.Read(read));
            Assert.AreEqual(0, pipe.Read(read));
            CollectionAssert.AreEqual(data, read);
            Assert.AreEqual(0, TransferMemory.Reserved, "Read memory goes back to the budget");
            Assert.IsTrue(pipe.Drained.IsCompleted);
        }
        finally
        {
            Data.Settings.TransferMemoryMB = AppSettings.TransferMemoryMBDefault;
        }
    }

    [TestMethod]
    public void SyncPipe_BorrowsNoMoreThanTheBudget()
    {
        Data.Settings.TransferMemoryMB = 1;

        try
        {
            var pipe = new SyncPipe(10 * 1024 * 1024, canBorrow: true);
            var writer = Task.Run(() => pipe.Write(new byte[4 * 1024 * 1024]));

            Assert.IsFalse(writer.Wait(300), "Past the budget the writer waits");
            Assert.IsLessThanOrEqualTo(1024L * 1024, TransferMemory.Reserved);

            // A closed reader frees what was buffered
            pipe.CloseReader();
            Assert.ThrowsExactly<AggregateException>(() => writer.Wait(2000));
            Assert.AreEqual(0, TransferMemory.Reserved);
        }
        finally
        {
            Data.Settings.TransferMemoryMB = AppSettings.TransferMemoryMBDefault;
        }
    }

    [TestMethod]
    public void StreamingTransfer_CopiesFileBetweenDevices()
    {
        var (source, target) = RequireTwoDevices();
        var file = $"{RemoteDir}/copy.bin";
        Shell(source, $"mkdir -p {RemoteDir} && head -c 33554437 /dev/urandom > {file} && touch -d '2020-01-02 03:04:05' {file}");
        Shell(target, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}");

        try
        {
            var op = Transfer(source, file, target, file);

            Assert.AreEqual(FileOperation.OperationStatus.Completed, op.Status, Describe(op));
            Assert.AreEqual(Sha256(source, file), Sha256(target, file));
            Assert.AreEqual(Shell(source, $"stat -c %Y {file}"), Shell(target, $"stat -c %Y {file}"));
        }
        finally
        {
            Shell(source, $"rm -rf {RemoteDir}");
            Shell(target, $"rm -rf {RemoteDir}");
        }
    }

    [TestMethod]
    public void StreamingTransfer_MissingSourceFailsWithoutTarget()
    {
        var (source, target) = RequireTwoDevices();
        var file = $"{RemoteDir}/missing.bin";
        Shell(source, $"rm -rf {RemoteDir}");
        Shell(target, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}");

        try
        {
            var op = Transfer(source, file, target, file);

            Assert.AreEqual(FileOperation.OperationStatus.Failed, op.Status, Describe(op));
            Assert.AreEqual("absent", Shell(target, $"ls {file} >/dev/null 2>&1 && echo present || echo absent"));
        }
        finally
        {
            Shell(target, $"rm -rf {RemoteDir}");
        }
    }

    [TestMethod]
    public void StreamingTransfer_CancelLeavesNoPartialFile()
    {
        var (source, target) = RequireTwoDevices();
        var file = $"{RemoteDir}/cancel.bin";
        Shell(source, $"mkdir -p {RemoteDir} && head -c 536870912 /dev/urandom > {file}");
        Shell(target, $"rm -rf {RemoteDir}; mkdir -p {RemoteDir}");

        try
        {
            var op = Transfer(source, file, target, file, cancelAfter: TimeSpan.FromSeconds(2));

            Assert.AreEqual(FileOperation.OperationStatus.Canceled, op.Status, Describe(op));
            Assert.AreEqual("absent", Shell(target, $"ls {file} >/dev/null 2>&1 && echo present || echo absent"));
        }
        finally
        {
            Shell(source, $"rm -rf {RemoteDir}");
            Shell(target, $"rm -rf {RemoteDir}");
        }
    }

    private static (DeviceData Source, DeviceData Target) RequireTwoDevices()
    {
        if (string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
            Assert.Inconclusive("adb was not found.");

        if (_devices.Length < 2)
            Assert.Inconclusive("Two online adb devices are required.");

        return (_devices[0], _devices[1]);
    }

    private static FileTransferOperation Transfer(DeviceData source, string sourcePath, DeviceData target, string targetPath, TimeSpan? cancelAfter = null)
    {
        var stat = Shell(source, $"stat -c '%s %Y' {sourcePath} 2>/dev/null").Split(' ');
        var file = new SyncFile(sourcePath);
        if (stat.Length == 2)
        {
            file.Size = long.Parse(stat[0]);
            file.UnixTime = double.Parse(stat[1]);
        }

        var op = new FileTransferOperation(
            file,
            new SyncFile(targetPath),
            ViewModel(source),
            ViewModel(target),
            isMove: false,
            Dispatcher.CurrentDispatcher);

        op.Start();

        var sw = Stopwatch.StartNew();
        while (op.Status is FileOperation.OperationStatus.InProgress && sw.Elapsed < TimeSpan.FromMinutes(5))
        {
            if (cancelAfter is { } delay && sw.Elapsed > delay)
            {
                op.Cancel();
                cancelAfter = null;
            }

            Thread.Sleep(100);
        }

        // StatusInfo is set through the dispatcher
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

        return op;
    }

    private static string Describe(FileTransferOperation op)
        => $"{(op.StatusInfo as FailedOpProgressViewModel)?.Error} {op.StatusInfo?.GetType().Name} | files: {string.Join("; ", op.FailedFiles.Select(f => $"{f.FullPath}: {(f.LastUpdate as SyncErrorInfo)?.Message}"))}";

    private static LogicalDeviceViewModel ViewModel(DeviceData device)
        => new(LogicalDevice.From(new DeviceSnapshot(device.Serial, device.Model, DeviceStatus.Ok, DeviceType.Local, RootStatus.Unchecked, "", device)));

    private static string Sha256(DeviceData device, string path) => Shell(device, $"sha256sum {path}").Split(' ')[0];

    private static string Shell(DeviceData device, string command)
    {
        var receiver = new ConsoleOutputReceiver();
        new AdbClient().ExecuteRemoteCommand($"{command}; true", device, receiver, Encoding.UTF8);

        return receiver.ToString().Trim();
    }
}
