#nullable enable

using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Test;

/// <summary>
/// Who reads a copied data object before anything is pasted: our file descriptors and contents go on the
/// real clipboard, and every request is logged with the process that made it. It replaces the clipboard's
/// content and empties it afterwards, so opt in with ADB_EXPLORER_PROBE=1. Run: dotnet test --filter ClipboardProbe
/// </summary>
[TestClass]
[DoNotParallelize]
public class ClipboardProbeTests
{
    private static readonly TimeSpan Observe = TimeSpan.FromSeconds(8);

    [TestMethod]
    public void ClipboardProbe_WhoReadsCopiedFiles()
    {
        if (Environment.GetEnvironmentVariable("ADB_EXPLORER_PROBE") != "1")
            Assert.Inconclusive("Set ADB_EXPLORER_PROBE=1 to run the probe; it uses the real clipboard.");

        Data.Settings = new AppSettings { KillAdbOnExit = false };
        List<string> log = [];

        RunSta(() =>
        {
            // Then with the format Windows documents for keeping content out of clipboard history and sync,
            // and last through WPF's Clipboard.SetDataObject, as the app copies
            foreach (var (exclude, viaWpf) in new[] { (false, false), (true, false), (false, true) })
            {
                var stage = (exclude, viaWpf) switch
                {
                    (true, _) => "excluded from history",
                    (_, true) => "WPF SetDataObject",
                    _ => "plain",
                };
                var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
                vfdo.SetFileDescriptors(new[] { "a.bin", "b.bin" }.Select(name => new FileDescriptor
                {
                    Name = name,
                    Length = 1000,
                    ChangeTimeUtc = DateTime.UtcNow,
                    Stream = () =>
                    {
                        Log(log, stage, $"opened the stream of {name}");
                        return SHCreateMemStream(new byte[1000], 1000);
                    },
                }).ToList());

                if (exclude)
                    vfdo.SetData(new AdbDataFormat("ExcludeClipboardContentFromMonitorProcessing"), [0, 0, 0, 0]);

                Log(log, stage, "set on the clipboard");
                var probe = new LoggingDataObject(vfdo, log, stage);
                if (viaWpf)
                    Clipboard.SetDataObject(probe);
                else
                    Assert.AreEqual(0, OleSetClipboard(probe));

                Pump(TimeSpan.FromSeconds(1));

                // What the app reads when the clipboard changes (GetClipboardPasteItems)
                Log(log, stage, "the app reads the clipboard");
                var clipboard = Clipboard.GetDataObject();
                Log(log, stage, $"formats: {string.Join(", ", clipboard.GetFormats())}");
                _ = clipboard.GetDataPresent(AdbDataFormats.AdbDrop);
                _ = clipboard.GetDataPresent(AdbDataFormats.FileDescriptor) && FileDescriptor.GetDescriptors(clipboard) is not null;
                _ = clipboard.GetDataPresent(AdbDataFormats.FileContents);
                _ = VirtualFileDataObject.GetPreferredDropEffect(clipboard);
                Log(log, stage, "the app is done reading");

                Pump(Observe - TimeSpan.FromSeconds(1));

                Assert.AreEqual(0, OleSetClipboard(null));
                Log(log, stage, "clipboard emptied");
                Pump(TimeSpan.FromMilliseconds(500));
            }
        });

        // Last, the data is left on the clipboard when OLE shuts down, as when the app exits
        RunSta(() =>
        {
            const string stage = "left at exit";
            var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.Clipboard);
            vfdo.SetFileDescriptors(new[] { "a.bin", "b.bin" }.Select(name => new FileDescriptor
            {
                Name = name,
                Length = 1000,
                Stream = () =>
                {
                    Log(log, stage, $"opened the stream of {name}");
                    return SHCreateMemStream(new byte[1000], 1000);
                },
            }).ToList());

            Log(log, stage, "set on the clipboard");
            Assert.AreEqual(0, OleSetClipboard(new LoggingDataObject(vfdo, log, stage)));
            Pump(TimeSpan.FromSeconds(1));

            // Balances RunSta's OleInitialize, so OLE really shuts down on this thread
            Log(log, stage, "OLE shuts down");
            OleUninitialize();
            Log(log, stage, "OLE is down");
        });

        // Whatever the exit left behind
        RunSta(() => OleSetClipboard(null));

        // Dragged over an Explorer folder without being dropped
        var folder = System.IO.Directory.CreateTempSubdirectory("adb-explorer-probe-").FullName;
        RunSta(() =>
        {
            const string stage = "hovering a folder";
            var vfdo = new VirtualFileDataObject(DragDropEffects.Copy, VirtualFileDataObject.DataObjectMethod.DragDrop);
            vfdo.SetFileDescriptors(new[] { "a.bin", "b.bin" }.Select(name => new FileDescriptor
            {
                Name = name,
                Length = 1000,
                Stream = () =>
                {
                    Log(log, stage, $"opened the stream of {name}");
                    return SHCreateMemStream(new byte[1000], 1000);
                },
            }).ToList());

            var target = GetDropTarget(folder);
            uint effect = 1; // DROPEFFECT_COPY
            Log(log, stage, "DragEnter");
            target.DragEnter(new LoggingDataObject(vfdo, log, stage), 1, default, ref effect);
            for (var i = 0; i < 20; i++)
            {
                effect = 1;
                target.DragOver(1, new POINTL { X = i, Y = i }, ref effect);
                Pump(TimeSpan.FromMilliseconds(50));
            }

            Log(log, stage, "DragLeave");
            target.DragLeave();
            Pump(TimeSpan.FromMilliseconds(500));
        });

        System.IO.Directory.Delete(folder, true);

        Console.WriteLine(string.Join(Environment.NewLine, log));
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static void Log(List<string> log, string stage, string message)
    {
        lock (log)
            log.Add($"{Clock.Elapsed.TotalSeconds,6:F2}s [{stage}] {message}");
    }

    /// <summary>The process behind the current call: the COM caller from another process, or whoever has the clipboard open.</summary>
    private static string Caller()
    {
        List<string> parts = [];
        if (CoGetCallerTID(out var tid) == 0)
            parts.Add($"COM from {ProcessOfThread(tid)}");

        var openWindow = GetOpenClipboardWindow();
        if (openWindow != 0)
        {
            GetWindowThreadProcessId(openWindow, out var pid);
            parts.Add($"clipboard opened by {ProcessName(pid)}");
        }

        if (parts.Count == 0)
            parts.Add("this process");

        return string.Join(", ", parts);
    }

    private static string ProcessOfThread(uint tid)
    {
        var thread = OpenThread(0x0800, false, tid); // THREAD_QUERY_LIMITED_INFORMATION
        if (thread == 0)
            return $"thread {tid}";

        try
        {
            return ProcessName(GetProcessIdOfThread(thread));
        }
        finally
        {
            CloseHandle(thread);
        }
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return $"{process.ProcessName} ({pid})";
        }
        catch (ArgumentException)
        {
            return $"process {pid}";
        }
    }

    private static string FormatName(short format) => DataFormats.GetDataFormat(format).Name;

    /// <summary>Passes every call on to the data object, logging the requests and who made them.</summary>
    private sealed class LoggingDataObject(VirtualFileDataObject inner, List<string> log, string stage) : ComDataObject, IAsyncOperation
    {
        private ComDataObject Inner => inner;

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            Log(log, stage, $"GetData {FormatName(format.cfFormat)} lindex {format.lindex} {format.tymed} by {Caller()}");
            Inner.GetData(ref format, out medium);
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            Log(log, stage, $"GetDataHere {FormatName(format.cfFormat)} by {Caller()}");
            Inner.GetDataHere(ref format, ref medium);
        }

        public int QueryGetData(ref FORMATETC format) => Inner.QueryGetData(ref format);

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            Log(log, stage, $"SetData {FormatName(formatIn.cfFormat)} by {Caller()}");
            Inner.SetData(ref formatIn, ref medium, release);
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            Log(log, stage, $"EnumFormatEtc by {Caller()}");
            return Inner.EnumFormatEtc(direction);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) => Inner.DAdvise(ref pFormatetc, advf, adviseSink, out connection);

        public void DUnadvise(int connection) => Inner.DUnadvise(connection);

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise) => Inner.EnumDAdvise(out enumAdvise);

        private IAsyncOperation Async => inner;

        public void SetAsyncMode(int fDoOpAsync) => Async.SetAsyncMode(fDoOpAsync);

        public void GetAsyncMode(out int pfIsOpAsync) => Async.GetAsyncMode(out pfIsOpAsync);

        public void StartOperation(IBindCtx pbcReserved)
        {
            Log(log, stage, $"StartOperation by {Caller()}");
            Async.StartOperation(pbcReserved);
        }

        public void InOperation(out int pfInAsyncOp) => Async.InOperation(out pfInAsyncOp);

        public void EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects)
        {
            Log(log, stage, $"EndOperation by {Caller()}");
            Async.EndOperation(hResult, pbcReserved, dwEffects);
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
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

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    private static IDropTarget GetDropTarget(string folder)
    {
        var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"); // IShellItem
        SHCreateItemFromParsingName(folder, 0, ref itemId, out var item);

        var bhid = new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"); // BHID_SFUIObject
        var targetId = typeof(IDropTarget).GUID;
        item.BindToHandler(0, ref bhid, ref targetId, out var target);

        return (IDropTarget)Marshal.GetObjectForIUnknown(target);
    }

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

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(ComDataObject? pDataObj);

    [DllImport("ole32.dll")]
    private static extern int CoGetCallerTID(out uint lpdwTID);

    [DllImport("user32.dll")]
    private static extern nint GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern nint OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetProcessIdOfThread(nint thread);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("shlwapi.dll")]
    private static extern nint SHCreateMemStream(byte[] pInit, uint cbInit);
}
