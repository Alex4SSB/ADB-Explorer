using ADB_Explorer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace ADB_Test;

[TestClass]
public class FileNameEncodingTests
{
    [TestMethod]
    [DataRow("a:b.txt", "a：b.txt")]
    [DataRow("<>:\"|?*\\", "＜＞：＂｜？＊＼")]
    [DataRow("trail.", "trail．")]
    [DataRow("trail ", "trail␠")]
    [DataRow("dots..", "dots.．")]
    [DataRow("CON.txt", "ＣON.txt")]
    [DataRow("nul", "ｎul")]
    [DataRow("lpt1 .log", "ｌpt1 .log")]
    [DataRow("CONSOLE.txt", "CONSOLE.txt")]
    [DataRow("tab\tnew\nline", "tab␉new␊line")]
    [DataRow("real：fullwidth", "real‛：fullwidth")]
    [DataRow("quote‛d", "quote‛‛d")]
    [DataRow("picture␊", "picture‛␊")]
    [DataRow("ƀíğ😀.bin", "ƀíğ😀.bin")]
    public void ToWindows_MapsWhatWindowsRefuses(string device, string windows)
        => Assert.AreEqual(windows, FileNameEncoding.ToWindows(device));

    [TestMethod]
    [DataRow("a:b.txt")]
    [DataRow("<>:\"|?*\\")]
    [DataRow("trail.")]
    [DataRow("trail ")]
    [DataRow("CON.txt")]
    [DataRow("tab\tnew\nline")]
    [DataRow("real：fullwidth")]
    [DataRow("quote‛d")]
    [DataRow("‛")]
    [DataRow("picture␊ and ␠")]
    [DataRow("ＣON")]
    [DataRow(".hidden")]
    public void ToDevice_RestoresTheDeviceName(string device)
    {
        var windows = FileNameEncoding.ToWindows(device)!;
        Assert.AreEqual(device, FileNameEncoding.ToDevice(windows));
        Assert.AreEqual(-1, windows.IndexOfAny([.. "<>:\"|?*\\"]), windows);
        Assert.IsFalse(windows.Any(char.IsControl), windows);
        Assert.IsFalse(windows.EndsWith('.') || windows.EndsWith(' '), windows);
    }

    [TestMethod]
    [DataRow("plain.txt", true)]
    [DataRow("real：fullwidth", true)]
    [DataRow("ƀíğ😀.bin", true)]
    [DataRow("CONSOLE.txt", true)]
    [DataRow("a:b.txt", false)]
    [DataRow("trail.", false)]
    [DataRow("trail ", false)]
    [DataRow("CON.txt", false)]
    [DataRow("tab\tline", false)]
    public void IsValidWindowsName_TellsWhatWindowsTakesAsIs(string name, bool valid)
        => Assert.AreEqual(valid, FileNameEncoding.IsValidWindowsName(name));

    [TestMethod]
    [DataRow("a:b.txt", "a：b.txt")]
    [DataRow("trail.", "trail．")]
    [DataRow("CON.txt", "ＣON.txt")]
    [DataRow("tab\tline", "tab␉line")]
    [DataRow("real：fullwidth", "real：fullwidth")]
    [DataRow("quote‛d", "quote‛d")]
    public void ForWindows_SubstitutesWithoutEscaping(string device, string windows)
        => Assert.AreEqual(windows, FileNameEncoding.ForWindows(device));

    [TestMethod]
    [DataRow("a:b?.txt", "a：b？.txt")]
    [DataRow("<>\"|*\\", "＜＞＂｜＊＼")]
    [DataRow("trail.", "trail.")]
    [DataRow("CON.txt", "CON.txt")]
    [DataRow("real：fullwidth", "real：fullwidth")]
    public void ForRestrictedNaming_SubstitutesOnlyTheCharactersItRefuses(string device, string restricted)
        => Assert.AreEqual(restricted, FileNameEncoding.ForRestrictedNaming(device));

    [TestMethod]
    public void ForPath_SubstitutesEachName()
        => Assert.AreEqual("q？d/x＊y.txt", FileNameEncoding.ForPath("q?d/x*y.txt", '/', FileNameEncoding.ForWindows));

    [TestMethod]
    public void TargetNames_FollowTheSetting()
    {
        var settings = ADB_Explorer.Models.Data.Settings;
        try
        {
            ADB_Explorer.Models.Data.Settings = new ADB_Explorer.Models.AppSettings { KillAdbOnExit = false, SubstituteIllegalNames = false };
            Assert.AreEqual("a:b", FileHelper.WindowsTargetName("a:b"));
            Assert.AreEqual("q?d/x*y", FileHelper.WindowsTargetRelativePath("q?d/x*y"));
            Assert.AreEqual("a:b", FileHelper.DeviceTargetName("a:b", restrictedNaming: true));

            ADB_Explorer.Models.Data.Settings.SubstituteIllegalNames = true;
            Assert.AreEqual("a：b", FileHelper.WindowsTargetName("a:b"));
            Assert.AreEqual("q？d/x＊y", FileHelper.WindowsTargetRelativePath("q?d/x*y"));
            Assert.AreEqual("a：b", FileHelper.DeviceTargetName("a:b", restrictedNaming: true));
            Assert.AreEqual("a:b", FileHelper.DeviceTargetName("a:b", restrictedNaming: false));
        }
        finally
        {
            ADB_Explorer.Models.Data.Settings = settings;
        }
    }

    [TestMethod]
    public void ToWindows_RefusesNamesTooLongForWindows()
    {
        Assert.IsNotNull(FileNameEncoding.ToWindows(new string('a', 255)));
        Assert.IsNull(FileNameEncoding.ToWindows(new string(':', 128) + new string('‛', 64)));
        Assert.IsNull(FileNameEncoding.ToWindows(""));
    }

    [TestMethod]
    public void CaseVariants_AreToldApartByWindowsAndMapBack()
    {
        var variants = FileNameEncoding.CaseVariants("Photo.JPG").ToList();
        CollectionAssert.AreEqual(new[] { "Ｐhoto.ＪＰＧ", "Pｈｏｔｏ.JPG", "Ｐｈｏｔｏ.ＪＰＧ" }, variants);

        foreach (var variant in variants)
        {
            Assert.IsFalse(string.Equals(variant, "photo.jpg", StringComparison.OrdinalIgnoreCase), variant);
            Assert.AreEqual("Photo.JPG", FileNameEncoding.ToDevice(variant));
        }

        Assert.AreEqual(3, variants.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.AreEqual(0, FileNameEncoding.CaseVariants("123.😀").Count());
    }

    [TestMethod]
    public void ToWindows_GivesNamesWindowsCreates()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"adbx-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var device in new[] { "a:b.txt", "trail.", "trail ", "CON.txt", "nul", "tab\tline", "<>:\"|?*\\" })
            {
                var windows = FileNameEncoding.ToWindows(device)!;
                File.WriteAllText(Path.Combine(folder, windows), device);
                Assert.IsTrue(Directory.EnumerateFiles(folder).Any(f => Path.GetFileName(f) == windows), $"{windows} wasn't created as named");
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
