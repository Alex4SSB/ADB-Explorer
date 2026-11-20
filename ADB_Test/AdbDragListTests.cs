#nullable enable

using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace ADB_Test;

[TestClass]
public class AdbDragListTests
{
    private static NativeMethods.AdbDragList? Read(byte[] bytes) => NativeMethods.AdbDragList.TryRead(new MemoryStream(bytes));

    [TestMethod]
    public void RoundTrip_KeepsAllFields()
    {
        NativeMethods.AdbDragList list = new("R58M123 ABC", "/sdcard/Ünïcode folder", ["a.txt", "b c.jpg", "日本.png"]);

        var read = Read([.. list.Bytes]);

        Assert.IsNotNull(read);
        Assert.AreEqual(list.DeviceId, read.DeviceId);
        Assert.AreEqual(list.ParentFolder, read.ParentFolder);
        CollectionAssert.AreEqual(list.Items, read.Items);
    }

    [TestMethod]
    public void ClipboardRoundTrip_IsReadBack()
    {
        NativeMethods.AdbDragList list = new("dev", "/sdcard", ["x.txt"]);
        NativeMethods.AdbDragList? read = null;
        string? type = null;

        var thread = new System.Threading.Thread(() =>
        {
            var format = ADB_Explorer.Models.AdbExplorerConst.ADB_DRAG_FORMAT;
            System.Windows.Clipboard.SetDataObject(new System.Windows.DataObject(format, new MemoryStream([.. list.Bytes])));
            var data = System.Windows.Clipboard.GetDataObject().GetData(format);
            type = data?.GetType().FullName;
            if (data is MemoryStream stream)
                read = NativeMethods.AdbDragList.TryRead(stream);
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNotNull(read, type);
    }

    [TestMethod]
    public void TrailingBytes_AreIgnored()
    {
        NativeMethods.AdbDragList list = new("dev", "/sdcard", ["x"]);

        // An HGLOBAL can be larger than what was written
        Assert.IsNotNull(Read([.. list.Bytes, 0, 0, 0, 0, 0, 0, 0]));
    }

    [TestMethod]
    public void IncompleteOrForeignData_IsRejected()
    {
        var bytes = new NativeMethods.AdbDragList("dev", "/sdcard", ["one", "two"]).Bytes.ToArray();

        Assert.IsNull(Read([]));
        Assert.IsNull(Read(new byte[64]));
        Assert.IsNull(Read([0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3]));

        for (int length = 1; length < bytes.Length; length++)
            Assert.IsNull(Read(bytes[..length]), $"truncated to {length} bytes");
    }
}
