using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ADB_Explorer.Platform.Linux;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class LinuxFileManagerNameTests
{
    private string _root = "";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "adbx-fm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    private string DataDir(string name) => Path.Combine(_root, name);

    private void WriteDesktopFile(string dataDir, string relativePath, string content)
    {
        var path = Path.Combine(DataDir(dataDir), "applications", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteMo(string localeDir, string locale, string domain, params (string Id, string Text)[] entries)
    {
        var path = Path.Combine(_root, localeDir, locale, "LC_MESSAGES", $"{domain}.mo");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, BuildMo(entries));
    }

    private FileManagerNameLookup Lookup(string desktopId)
        => new([DataDir("home"), DataDir("usr")], [Path.Combine(_root, "langpack"), Path.Combine(_root, "locale")], () => desktopId);

    private const string Ubuntu = """
        [Desktop Entry]
        Name=Files
        Comment=Access and organize files
        X-Ubuntu-Gettext-Domain=nautilus

        [Desktop Action new-window]
        Name=New Window
        """;

    [TestMethod]
    public void UsesNameLines_WhenTheDistributionKeepsThem()
    {
        WriteDesktopFile("usr", "org.kde.dolphin.desktop", """
            [Desktop Entry]
            Name=Dolphin
            Name[ru]=Dolphin RU
            Name[pt_BR]=Dolphin BR
            """);

        var lookup = Lookup("org.kde.dolphin.desktop");

        Assert.AreEqual("Dolphin RU", lookup.GetName(Culture("ru-RU")));
        Assert.AreEqual("Dolphin BR", lookup.GetName(Culture("pt-BR")));
        Assert.AreEqual("Dolphin", lookup.GetName(Culture("en-US")));
        Assert.IsNull(lookup.GetName(Culture("de-DE")));
    }

    [TestMethod]
    public void FallsBackToGettext_WhenNameLinesAreStripped()
    {
        WriteDesktopFile("usr", "org.gnome.Nautilus.desktop", Ubuntu);
        WriteMo("langpack", "ru", "nautilus", ("Files", "Файлы"), ("New Window", "Новое окно"));
        WriteMo("locale", "de", "nautilus", ("Files", "Dateien"));

        var lookup = Lookup("org.gnome.Nautilus.desktop");

        Assert.AreEqual("Файлы", lookup.GetName(Culture("ru-RU")));
        Assert.AreEqual("Dateien", lookup.GetName(Culture("de")));
        Assert.AreEqual("Files", lookup.GetName(Culture("en")));
        Assert.IsNull(lookup.GetName(Culture("he-IL")), "no translation installed: the app's own string is used");
    }

    [TestMethod]
    public void PrefersUserDataDir_AndVendorSubfolder()
    {
        WriteDesktopFile("usr", "org.gnome.Nautilus.desktop", "[Desktop Entry]\nName=System");
        WriteDesktopFile("home", "org.gnome.Nautilus.desktop", "[Desktop Entry]\nName=User");
        WriteDesktopFile("usr", "kde4/dolphin.desktop", "[Desktop Entry]\nName=Old Dolphin");

        Assert.AreEqual("User", Lookup("org.gnome.Nautilus.desktop").GetName(Culture("en")));
        Assert.AreEqual("Old Dolphin", Lookup("kde4-dolphin.desktop").GetName(Culture("en")));
    }

    [TestMethod]
    public void ReturnsNull_WithoutADefaultFileManager()
    {
        Assert.IsNull(Lookup(null).GetName(Culture("en")));
        Assert.IsNull(Lookup("missing.desktop").GetName(Culture("en")));
    }

    [TestMethod]
    public void DesktopEntry_UnescapesValues_AndIgnoresOtherGroups()
    {
        var entry = DesktopEntry.Parse(["# comment", "[Desktop Entry]", @"Name=A\sB\\C", "[Desktop Action x]", "Name=Other"]);

        Assert.AreEqual(@"A B\C", entry.Name);
    }

    [TestMethod]
    public void MoCatalog_ReadsBothByteOrders_AndPluralKeys()
    {
        var little = BuildMo([("Files", "Файлы"), ("item\0items", "элемент\0элемента")]);
        var big = BuildMo([("Files", "Файлы")], bigEndian: true);

        Assert.AreEqual("Файлы", MoCatalog.Translate(little, "Files"));
        Assert.AreEqual("элемент", MoCatalog.Translate(little, "item"));
        Assert.AreEqual("Файлы", MoCatalog.Translate(big, "Files"));
        Assert.IsNull(MoCatalog.Translate(little, "Missing"));
        Assert.IsNull(MoCatalog.Translate([1, 2, 3], "Files"));
    }

    [TestMethod]
    public void LocaleNames_GoFromRegionToLanguage()
    {
        CollectionAssert.AreEqual(new[] { "pt_BR", "pt" }, FileManagerNameLookup.LocaleNames(Culture("pt-BR")).ToArray());
        CollectionAssert.AreEqual(new[] { "ru" }, FileManagerNameLookup.LocaleNames(Culture("ru")).ToArray());
    }

    /// <summary>A GNU .mo file; real ones also hold a header entry and a hash table, which readers may ignore.</summary>
    private static byte[] BuildMo(IEnumerable<(string Id, string Text)> entries, bool bigEndian = false)
    {
        var sorted = entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var ids = sorted.Select(e => Encoding.UTF8.GetBytes(e.Id)).ToList();
        var texts = sorted.Select(e => Encoding.UTF8.GetBytes(e.Text)).ToList();

        const int header = 28;
        var originalsTable = header;
        var translationsTable = originalsTable + sorted.Count * 8;
        var dataStart = translationsTable + sorted.Count * 8;

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        void Write(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (bigEndian)
                Array.Reverse(bytes);

            writer.Write(bytes);
        }

        Write(0x950412de);
        Write(0);
        Write((uint)sorted.Count);
        Write((uint)originalsTable);
        Write((uint)translationsTable);
        Write(0);
        Write(0);

        var offset = dataStart;
        foreach (var strings in new[] { ids, texts })
        {
            foreach (var value in strings)
            {
                Write((uint)value.Length);
                Write((uint)offset);
                offset += value.Length + 1;
            }
        }

        foreach (var value in ids.Concat(texts))
        {
            writer.Write(value);
            writer.Write((byte)0);
        }

        return stream.ToArray();
    }
}
