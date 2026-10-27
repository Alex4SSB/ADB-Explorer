#nullable enable

using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ADB_Test;

/// <summary>
/// Drags that hold only FileDrop: files that already exist, 7-Zip's (extracted at the drop, to a temp folder it then
/// removes), and async sources like the new Outlook, read through the source's own data object after the drop.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FileDropTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // App's static ctor (reached through App.SafeInvoke) would kill adb when the test host exits
        Data.Settings = new AppSettings { KillAdbOnExit = false };
    }

    [TestMethod]
    public void FileDrop_AcceptedWhenFilesExist()
    {
        var local = NewDir();
        var existing = Path.Combine(local, "attachment.pdf");
        File.WriteAllBytes(existing, [1, 2, 3]);

        try
        {
            var dropped = new DataObject(DataFormats.FileDrop, new[] { existing });
            CollectionAssert.AreEqual(new[] { existing }, CopyPasteService.GetExistingDropFiles(dropped));

            // A target-detect source names a file it only creates once it sees the drop target
            var targetDetect = new DataObject(DataFormats.FileDrop, new[] { existing, Path.Combine(local, "later", "file.txt") });
            Assert.IsNull(CopyPasteService.GetExistingDropFiles(targetDetect));
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_SevenZipItemsAreMovedOutOfItsTempFolder()
    {
        // As 7-Zip leaves them at the drop: extracted to %TEMP%\7zE*, which it deletes once the drop returns
        var sevenZipTemp = Path.Combine(Path.GetTempPath(), $"7zE{Random.Shared.Next():X8}");
        var file = Path.Combine(sevenZipTemp, "doc.txt");
        var folder = Path.Combine(sevenZipTemp, "dir");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(file, "hello");
        File.WriteAllText(Path.Combine(folder, "sub", "a.txt"), "a");

        try
        {
            var dropped = CopyPasteService.GetDroppedFiles(new DataObject(DataFormats.FileDrop, new[] { file, folder }));

            Assert.IsNotNull(dropped);
            Assert.IsFalse(File.Exists(file) || Directory.Exists(folder), "Moved, not copied");
            Assert.AreEqual("hello", File.ReadAllText(dropped[0]));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(dropped[1], "sub", "a.txt")));
            Assert.IsFalse(dropped.Any(path => path.StartsWith(sevenZipTemp, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(sevenZipTemp, true);
        }
    }

    [TestMethod]
    public void FileDrop_OtherSourcesKeepTheirFiles()
    {
        var local = NewDir();
        var file = Path.Combine(local, "kept.txt");
        File.WriteAllText(file, "kept");

        try
        {
            CollectionAssert.AreEqual(new[] { file }, CopyPasteService.GetDroppedFiles(new DataObject(DataFormats.FileDrop, new[] { file })));
            Assert.IsTrue(File.Exists(file));
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_SevenZipFolderDragKeepsUserFiles()
    {
        // 7-Zip offers its format for files dragged from a regular folder too, and those are the user's own
        var local = NewDir();
        var file = Path.Combine(local, "mine.txt");
        File.WriteAllText(file, "mine");

        try
        {
            var dropped = new DataObject(DataFormats.FileDrop, new[] { file });
            dropped.SetData("7-Zip::GetTransfer", new MemoryStream(new byte[16]));

            CollectionAssert.AreEqual(new[] { file }, CopyPasteService.GetDroppedFiles(dropped));
            Assert.IsTrue(File.Exists(file), "Not moved");

            var service = new CopyPasteService();
            service.PreviewDataObject(dropped);
            Assert.IsFalse(service.HasUnnamedFiles);
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_PathsSteppingUpAreRefused()
    {
        var local = NewDir();
        var file = Path.Combine(local, "kept.txt");
        Directory.CreateDirectory(Path.Combine(local, "sub"));
        File.WriteAllText(file, "kept");

        // Exists, but names a folder above the one it's in
        var steppingUp = Path.Combine(local, "sub", "..", "kept.txt");

        try
        {
            CollectionAssert.AreEqual(new[] { file }, CopyPasteService.GetExistingDropFiles(new DataObject(DataFormats.FileDrop, new[] { file, steppingUp })));
            Assert.IsNull(CopyPasteService.GetExistingDropFiles(new DataObject(DataFormats.FileDrop, new[] { steppingUp })));
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_SevenZipDragHasUnnamedFiles()
    {
        // While dragging, 7-Zip names only its still empty temp folder
        var sevenZipTemp = Path.Combine(Path.GetTempPath(), $"7zE{Random.Shared.Next():X8}");
        Directory.CreateDirectory(sevenZipTemp);

        try
        {
            var service = new CopyPasteService();
            service.PreviewDataObject(new DataObject(DataFormats.FileDrop, new[] { sevenZipTemp }));

            Assert.IsTrue(service.HasUnnamedFiles);
            Assert.IsTrue(service.IsFileDropOnly);
            Assert.IsFalse(service.CurrentFiles.Any(), "Its temp folder isn't what's dragged");
            Assert.HasCount(1, service.DragFiles);
        }
        finally
        {
            Directory.Delete(sevenZipTemp, true);
        }
    }

    [TestMethod]
    public void FileDrop_ExistingFilesAreNamed()
    {
        var local = NewDir();
        var file = Path.Combine(local, "kept.txt");
        File.WriteAllText(file, "kept");

        try
        {
            var service = new CopyPasteService();
            service.PreviewDataObject(new DataObject(DataFormats.FileDrop, new[] { file }));

            Assert.IsFalse(service.HasUnnamedFiles);
            CollectionAssert.AreEqual(new[] { file }, service.DragFiles);
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    /// <summary>A source like the new Outlook (Chromium): its file exists only once asked for after the drag, in async mode.</summary>
    [TestMethod]
    public void FileDrop_AsyncSourceIsReadAfterTheDrop()
    {
        var local = NewDir();

        try
        {
            var source = new ChromiumLikeSource(Path.Combine(local, "attachment.pdf"));

            // What WPF's drop target hands a drop handler
            var wrapped = new DataObject(source);

            Assert.IsNull(CopyPasteService.GetExistingDropFiles(wrapped), "Refused during the drag");
            Assert.AreSame(source, CopyPasteService.GetSourceDataObject(wrapped));
            Assert.IsTrue(CopyPasteService.IsAsyncSource(wrapped));

            CopyPasteService.PushDelayedFileDrop(wrapped, "/sdcard", null!);

            // The drop returns, and with it the source's drag loop
            Thread.Sleep(300);
            source.InDragLoop = false;

            Assert.IsTrue(source.Ended.Wait(TimeSpan.FromSeconds(10)), "EndOperation");
            Assert.AreEqual(0, source.EndResult);
            Assert.IsTrue(source.WasStarted, "StartOperation came before the file was asked for");
            Assert.IsTrue(File.Exists(source.FilePath), "The source wrote its file when asked");
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    /// <summary>Chromium's DataObjectImpl as far as a drop target sees it, for a dragged download.</summary>
    private sealed class ChromiumLikeSource(string filePath) : ComDataObject, IAsyncOperation
    {
        private static readonly short HDrop = (short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id;

        public string FilePath => filePath;
        public volatile bool InDragLoop = true;
        public volatile bool WasStarted;
        public int EndResult = -1;
        public readonly ManualResetEventSlim Ended = new();

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = default;
            if (format.cfFormat != HDrop || InDragLoop || !WasStarted)
                throw new COMException("DV_E_FORMATETC", unchecked((int)0x80040064));

            // The download
            File.WriteAllBytes(filePath, [1, 2, 3]);

            var bytes = new NativeMethods.CFHDROP([filePath]).Bytes.ToArray();
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, medium.unionmember, bytes.Length);
        }

        public int QueryGetData(ref FORMATETC format) => format.cfFormat == HDrop ? 0 : unchecked((int)0x80040064);

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            SHCreateStdEnumFmtEtc(1, [new() { cfFormat = HDrop, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL }], out var formats);

            return formats;
        }

        public void SetAsyncMode(int fDoOpAsync) { }
        public void GetAsyncMode(out int pfIsOpAsync) => pfIsOpAsync = -1;
        public void StartOperation(IBindCtx pbcReserved) => WasStarted = true;
        public void InOperation(out int pfInAsyncOp) => pfInAsyncOp = WasStarted ? -1 : 0;

        public void EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects)
        {
            EndResult = hResult;
            Ended.Set();
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) { formatOut = formatIn; return unchecked((int)0x80004001); }
        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) { }
        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) { connection = 0; return unchecked((int)0x80040003); }
        public void DUnadvise(int connection) { }
        public int EnumDAdvise(out IEnumSTATDATA enumAdvise) { enumAdvise = null!; return unchecked((int)0x80040003); }

        [DllImport("shell32.dll")]
        private static extern int SHCreateStdEnumFmtEtc(uint cfmt, FORMATETC[] afmt, out IEnumFORMATETC ppenumFormatEtc);
    }

    [TestMethod]
    public void FileDrop_SyncSourceIsNotTakenAsAsync()
    {
        var local = NewDir();
        var file = Path.Combine(local, "a.txt");
        File.WriteAllText(file, "a");

        try
        {
            RunSta(() =>
            {
                // The shell's own data objects don't extract asynchronously
                Assert.IsFalse(CopyPasteService.IsAsyncSource(new DataObject(ShellDataObject(file))));

                // Our own data objects have no source behind them
                Assert.IsNull(CopyPasteService.GetSourceDataObject(new DataObject(DataFormats.FileDrop, new[] { file })));
            });
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    private static ComDataObject ShellDataObject(string path)
    {
        var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        SHCreateItemFromParsingName(path, 0, ref itemId, out var item);

        var bhid = new Guid("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4"); // BHID_DataObject
        var dataObjectId = typeof(ComDataObject).GUID;
        item.BindToHandler(0, ref bhid, ref dataObjectId, out var pointer);

        var dataObject = (ComDataObject)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);

        return dataObject;
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"adb-explorer-test-drop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        return dir;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, nint pbc, ref Guid riid, out IShellItem ppv);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
    }
}
