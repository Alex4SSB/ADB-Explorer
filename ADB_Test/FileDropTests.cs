#nullable enable

using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        string? staging = null;
        try
        {
            var dropped = CopyPasteService.GetDroppedFiles(new DataObject(DataFormats.FileDrop, new[] { file, folder }), out staging);

            Assert.IsNotNull(dropped);
            Assert.IsNotNull(staging);
            Assert.IsFalse(File.Exists(file) || Directory.Exists(folder), "Moved, not copied");
            Assert.AreEqual("hello", File.ReadAllText(dropped[0]));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(dropped[1], "sub", "a.txt")));
            Assert.IsTrue(dropped.All(path => path.StartsWith(staging, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(sevenZipTemp, true);
            if (staging is not null)
                Directory.Delete(staging, true);
        }
    }

    [TestMethod]
    public void FileDrop_WinRarDragListsItsItems()
    {
        // While dragging, WinRAR names its items relative to the archive root; nothing is extracted yet
        var service = new CopyPasteService();
        service.PreviewDataObject(new DataObject(DataFormats.FileDrop, new[] { "top.txt", @"folder\sub" }));

        Assert.IsFalse(service.HasUnnamedFiles, "Its items are listed by name");
        Assert.IsTrue(service.HasPendingFiles);
        Assert.IsTrue(service.IsFileDropOnly);
        Assert.IsFalse(service.IsDelayedFileDrop, "Its files are named again at the drop, not after it");
        CollectionAssert.AreEqual(new[] { "top.txt", "sub" }, service.DragFiles);

        var current = service.CurrentFiles.ToArray();
        CollectionAssert.AreEqual(new[] { "top.txt", "sub" }, current.Select(f => f.FullName).ToArray());
        Assert.IsFalse(current[0].IsDirectory);
        Assert.IsTrue(current[1].IsDirectory, "No extension, so most likely a folder");

        Assert.IsNull(CopyPasteService.GetRelativeDropNames(new DataObject(DataFormats.FileDrop, new[] { @"..\top.txt" })));
    }

    [TestMethod]
    public void FileDrop_WinRarItemsAreMovedOutOfItsTempFolder()
    {
        // At the drop WinRAR extracts to %TEMP%\Rar$DRa<pid>.<n>.rartemp and names the files there; it removes them when it closes
        var winRarTemp = Path.Combine(Path.GetTempPath(), $"Rar$DRa{Random.Shared.Next(100000)}.{Random.Shared.Next(100000)}.rartemp");
        var file = Path.Combine(winRarTemp, "top.txt");
        var folder = Path.Combine(winRarTemp, "folder");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(file, "top");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "b");

        string? staging = null;
        try
        {
            var dropped = CopyPasteService.GetDroppedFiles(new DataObject(DataFormats.FileDrop, new[] { file, folder }), out staging);

            Assert.IsNotNull(dropped);
            Assert.IsNotNull(staging);
            Assert.IsFalse(File.Exists(file) || Directory.Exists(folder), "Moved, not copied");
            Assert.AreEqual("top", File.ReadAllText(dropped[0]));
            Assert.AreEqual("b", File.ReadAllText(Path.Combine(dropped[1], "sub", "b.txt")));
        }
        finally
        {
            Directory.Delete(winRarTemp, true);
            if (staging is not null)
                Directory.Delete(staging, true);
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

    [TestMethod]
    public void FileDrop_ChromiumCustomDataIsRead()
    {
        var data = ChromiumCustomData(
            ("chromium/x-drag-id", "D74021194D492181FA126277D0926F95"),
            ("attachment", AttachmentJson("1000090559.jpg", "notes")));

        var entries = CopyPasteService.ParseChromiumCustomData(data);
        Assert.AreEqual("D74021194D492181FA126277D0926F95", entries["chromium/x-drag-id"]);
        CollectionAssert.AreEqual(new[] { "1000090559.jpg", "notes" }, CopyPasteService.GetChromiumAttachmentNames(entries));

        Assert.IsEmpty(CopyPasteService.ParseChromiumCustomData(data[..^10]), "Cut short");

        // Whole emails, and names that aren't plain file names, aren't taken
        Assert.IsNull(CopyPasteService.GetChromiumAttachmentNames(new Dictionary<string, string> { ["attachment"] = """{"itemType":"multimaillistconversationrows"}""" }));
        Assert.IsNull(CopyPasteService.GetChromiumAttachmentNames(new Dictionary<string, string> { ["attachment"] = AttachmentJson(@"..\escape.jpg") }));
        Assert.IsNull(CopyPasteService.GetChromiumAttachmentNames(new Dictionary<string, string> { ["attachment"] = "not json" }));
    }

    [TestMethod]
    public void FileDrop_OutlookThumbnailIsNotTheFile()
    {
        var local = NewDir();

        try
        {
            // Its FileGroupDescriptor and FileContents are a thumbnail named by a random ID; the image comes through FileDrop
            var source = new ChromiumLikeSource(Path.Combine(local, "1000090458.jpg"),
                ChromiumCustomData(("attachment", AttachmentJson("1000090458.jpg"))), hasThumbnail: true);

            var service = new CopyPasteService();
            service.PreviewDataObject(new DataObject(source));

            Assert.IsTrue(service.IsDelayedFileDrop, "Read from FileDrop after the drop");
            Assert.IsEmpty(service.Descriptors, "The thumbnail's descriptor isn't used");
            CollectionAssert.AreEqual(new[] { "1000090458.jpg" }, service.DragFiles);

            // A drag of the thumbnail alone has no file to give, so it's refused
            var thumbnailOnly = new DataObject(new ChromiumLikeSource(Path.Combine(local, "x.jpg"),
                ChromiumCustomData(("attachment", AttachmentJson("1000090458.jpg"))), hasThumbnail: true, hasFileDrop: false));

            Assert.IsTrue(CopyPasteService.IsOutlookDescribedDrag(thumbnailOnly));
            Assert.IsFalse(new CopyPasteService().PreviewFileDrop(thumbnailOnly));

            // Its image still serves as the drag image and the dropped image's thumbnail. Its descriptor gives no size
            // (nor reading it throws), so the content's own size decides whether it's read
            Assert.IsNull(CopyPasteService.GetDragThumbnailSize(new DataObject(source)));
            Assert.IsNull(CopyPasteService.ReadDragThumbnail(source, ChromiumLikeSource.ThumbnailPng.Length - 1));

            var thumbnail = CopyPasteService.ReadDragThumbnail(source, ChromiumLikeSource.ThumbnailPng.Length);
            Assert.IsNotNull(thumbnail);
            Assert.AreEqual(3, thumbnail.PixelWidth);
            Assert.AreEqual(2, thumbnail.PixelHeight);

            // Read off the UI thread, it must still encode on another, as seeding it does
            var readOff = Task.Run(() => CopyPasteService.ReadDragThumbnail(source, ChromiumLikeSource.ThumbnailPng.Length)).Result;
            Assert.IsNotNull(readOff);
            new PngBitmapEncoder().Frames.Add(BitmapFrame.Create(readOff));

            // A photo within the limit is its own thumbnail once pushed; a heavier one keeps the drag's
            var photo = Path.Combine(local, "photo.png");
            File.WriteAllBytes(photo, PngOf(5, 4));
            Assert.AreEqual(5, ThumbnailService.ChoosePushThumbnail(photo, thumbnail, new FileInfo(photo).Length).PixelWidth);
            Assert.AreSame(thumbnail, ThumbnailService.ChoosePushThumbnail(photo, thumbnail, new FileInfo(photo).Length - 1));

            var staged = Path.Combine(local, "staging", "1000090458.jpg");
            ThumbnailService.RegisterPushThumbnail(staged, thumbnail);
            Assert.AreSame(thumbnail, ThumbnailService.TakePushThumbnail(staged));
            Assert.IsNull(ThumbnailService.TakePushThumbnail(staged), "Taken once");

            ThumbnailService.RegisterPushThumbnail(staged, thumbnail);
            ThumbnailService.ForgetPushThumbnails(Path.Combine(local, "staging"));
            Assert.IsNull(ThumbnailService.TakePushThumbnail(staged), "Forgotten with its staging folder");
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_OutlookAttachmentsAreNamedCopies()
    {
        var local = NewDir();

        try
        {
            var source = new ChromiumLikeSource(Path.Combine(local, "attachment.pdf"),
                ChromiumCustomData(("attachment", AttachmentJson("report.pdf", "README"))));

            var service = new CopyPasteService();
            service.PreviewDataObject(new DataObject(source));

            Assert.IsFalse(service.HasUnnamedFiles, "Listed by the names it describes");
            Assert.IsTrue(service.HasPendingFiles);
            Assert.IsTrue(service.IsDelayedFileDrop, "Still read once it has written them");
            Assert.IsTrue(service.CurrentSource.HasFlag(CopyPasteService.DataSource.Virtual), "Copied, never moved");
            CollectionAssert.AreEqual(new[] { "report.pdf", "README" }, service.DragFiles);
            Assert.IsFalse(service.CurrentFiles.Any(file => file.IsDirectory), "Attachments are files, extension or not");
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void FileDrop_OutlookEmailsAreRefused()
    {
        var local = NewDir();

        try
        {
            // Its FileDrop is there, but nothing is written for it after the drop
            var source = new ChromiumLikeSource(Path.Combine(local, "mail.eml"),
                ChromiumCustomData(("attachment", """{"itemType":"multimaillistconversationrows"}""")));

            // Refused, which leaves the drag with no files (and so no drop effect)
            var service = new CopyPasteService();
            Assert.IsFalse(service.PreviewFileDrop(new DataObject(source)));
            Assert.IsFalse(service.IsFileDropOnly);
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    private static byte[] PngOf(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var png = new MemoryStream();
        encoder.Save(png);

        return png.ToArray();
    }

    private static string AttachmentJson(params string[] names)
        => JsonSerializer.Serialize(new
        {
            itemType = "attachment",
            attachmentFiles = names.Select(name => new { name, size = 1234, ContentType = "application/octet-stream" }),
        });

    /// <summary>Chromium's custom data pickle: payload size, entry count, then each type and data as UTF-16 padded to 4 bytes.</summary>
    private static byte[] ChromiumCustomData(params (string Type, string Data)[] entries)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((uint)entries.Length);
        foreach (var (type, data) in entries)
        {
            foreach (var text in new[] { type, data })
            {
                writer.Write(text.Length);
                writer.Write(Encoding.Unicode.GetBytes(text));
                if (text.Length % 2 != 0)
                    writer.Write((short)0);
            }
        }

        writer.Flush();
        return [.. BitConverter.GetBytes((uint)payload.Length), .. payload.ToArray()];
    }

    /// <summary>Chromium's DataObjectImpl as far as a drop target sees it, for a dragged download.</summary>
    /// <param name="hasThumbnail">Also offers a FileGroupDescriptor and FileContents, as the new Outlook does for a thumbnail.</param>
    private sealed class ChromiumLikeSource(string filePath, byte[]? customData = null, bool hasThumbnail = false, bool hasFileDrop = true)
        : ComDataObject, IAsyncOperation
    {
        private static readonly short HDrop = (short)DataFormats.GetDataFormat(DataFormats.FileDrop).Id;
        private static readonly short CustomData = (short)DataFormats.GetDataFormat("Chromium Web Custom MIME Data Format").Id;
        private static readonly short GroupDescriptor = (short)DataFormats.GetDataFormat("FileGroupDescriptorW").Id;
        private static readonly short Contents = (short)DataFormats.GetDataFormat("FileContents").Id;

        // A 3x2 image, the thumbnail's content
        public static readonly byte[] ThumbnailPng = PngOf(3, 2);

        // Formats it offers; the thumbnail's are never read by the app, so asking for them fails the test
        private bool Offers(short format)
            => (format == HDrop && hasFileDrop)
                || (format == CustomData && customData is not null)
                || ((format == GroupDescriptor || format == Contents) && hasThumbnail);

        public string FilePath => filePath;
        public volatile bool InDragLoop = true;
        public volatile bool WasStarted;
        public int EndResult = -1;
        public readonly ManualResetEventSlim Ended = new();

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = default;

            // What the page set for the drag, there during it, and the thumbnail (never taken as the dragged file)
            byte[] bytes;
            if (format.cfFormat == CustomData && customData is not null)
                bytes = customData;
            else if (format.cfFormat == Contents && hasThumbnail)
                bytes = ThumbnailPng;
            else if (format.cfFormat == GroupDescriptor && hasThumbnail)
                bytes = ThumbnailDescriptor();
            else if (format.cfFormat != HDrop || !hasFileDrop || InDragLoop || !WasStarted)
                throw new COMException("DV_E_FORMATETC", unchecked((int)0x80040064));
            else
            {
                // The download
                File.WriteAllBytes(filePath, [1, 2, 3]);
                bytes = new NativeMethods.CFHDROP([filePath]).Bytes.ToArray();
            }

            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, medium.unionmember, bytes.Length);
        }

        public int QueryGetData(ref FORMATETC format) => Offers(format.cfFormat) ? 0 : unchecked((int)0x80040064);

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            FORMATETC[] available = [.. new[] { HDrop, CustomData, GroupDescriptor, Contents }
                .Where(Offers)
                .Select(format => new FORMATETC
                {
                    cfFormat = format,
                    dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = -1,
                    tymed = TYMED.TYMED_HGLOBAL,
                })];

            SHCreateStdEnumFmtEtc((uint)available.Length, available, out var formats);

            return formats;
        }

        // Chromium's: only FD_LINKUI set, so no size, and its unflagged fields may hold anything (an unrepresentable time here)
        private static byte[] ThumbnailDescriptor()
        {
            var bytes = new byte[4 + 592];
            BitConverter.GetBytes(1u).CopyTo(bytes, 0);
            BitConverter.GetBytes(0x8000u).CopyTo(bytes, 4);
            BitConverter.GetBytes(-1L).CopyTo(bytes, 4 + 56);
            Encoding.Unicode.GetBytes("0a771a4e-3057-47f8-b955-6152d261ce4f.jpg").CopyTo(bytes, 4 + 72);

            return bytes;
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
