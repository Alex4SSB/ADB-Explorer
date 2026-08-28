using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace ADB_Test;

[TestClass]
public class TrashIndexerTests
{
    [TestMethod]
    public void TryParse_PlainPipes_ReadsOriginalPathAndName()
    {
        var ok = TrashIndexer.TryParse("{1712345678901}|/sdcard/DCIM/photo.jpg|2024.01.15-12:00:00", out var indexer);

        Assert.IsTrue(ok);
        Assert.AreEqual("{1712345678901}", indexer.RecycleName);
        Assert.AreEqual("/sdcard/DCIM/photo.jpg", indexer.OriginalPath);
        Assert.AreEqual("/sdcard/DCIM", indexer.ParentPath);
        Assert.IsTrue(indexer.MatchesRecycleFile("{1712345678901}"));
    }

    [TestMethod]
    public void TryParse_EchoEscapedPipes_StripsTrailingBackslashes()
    {
        var ok = TrashIndexer.TryParse(@"{1712345678901}\|/sdcard/Download/file.txt\|2024.01.15-12:00:00", out var indexer);

        Assert.IsTrue(ok);
        Assert.AreEqual("{1712345678901}", indexer.RecycleName);
        Assert.AreEqual("/sdcard/Download/file.txt", indexer.OriginalPath);
        Assert.AreEqual("/sdcard/Download", indexer.ParentPath);
        Assert.IsTrue(indexer.MatchesRecycleFile("{1712345678901}"));
    }

    [TestMethod]
    public void ParseLines_SkipsMalformedAndKeepsValid()
    {
        var parsed = TrashIndexer.ParseLines("""
            not-an-index
            {1}|/sdcard/a/b.txt|2024.01.15-12:00:00
            {2}\|/sdcard/c/d.txt\|2024.01.15-12:00:00
            """);

        Assert.AreEqual(2, parsed.Count);
        Assert.AreEqual("b.txt", FileHelper.GetFullName(parsed[0].OriginalPath));
        Assert.AreEqual("d.txt", FileHelper.GetFullName(parsed[1].OriginalPath));
    }

    [TestMethod]
    public void ParseLines_ResolvesOriginalPathThroughReRecycleChain()
    {
        var parsed = TrashIndexer.ParseLines("""
            {1784377459157}|/data/local/tmp/.studio|2026.07.18-15:24:19
            {1787591191222}|/sdcard/.Trash-AdbExplorer/{1784377459157}|2026.08.24-20:06:31
            {1787591191269}|/storage/emulated/0/.Trash-AdbExplorer/{1784377748394}|2026.08.24-20:06:31
            {1784377748394}|/data/local/tmp/android|2026.07.18-15:29:08
            """);

        var current = parsed.First(i => i.RecycleName == "{1787591191222}");
        Assert.AreEqual("/data/local/tmp/.studio", current.OriginalPath);
        Assert.AreEqual("/data/local/tmp", current.ParentPath);
        Assert.AreEqual(".studio", FileHelper.GetFullName(current.OriginalPath));

        var other = parsed.First(i => i.RecycleName == "{1787591191269}");
        Assert.AreEqual("/data/local/tmp/android", other.OriginalPath);
    }
}
