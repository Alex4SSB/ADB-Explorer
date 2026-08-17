using ADB_Explorer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Test;

[TestClass]
public class AppBackupTests
{
    [TestMethod]
    public void IsApkBackup_RecognizesExtensionOnly()
    {
        Assert.IsTrue(AppBackupHelper.IsApkBackup("com.myapp.apkbkp"));
        Assert.IsTrue(AppBackupHelper.IsApkBackup(@"C:\Backups\com.myapp.apkbkp"));
        Assert.IsTrue(AppBackupHelper.IsApkBackup("/sdcard/com.myapp.APKBKP"));
        Assert.IsFalse(AppBackupHelper.IsApkBackup("com.myapp.apk"));
        Assert.IsFalse(AppBackupHelper.IsApkBackup("com.myapp.tar.gz"));
        Assert.IsFalse(AppBackupHelper.IsApkBackup("archive.zip"));
        Assert.IsFalse(AppBackupHelper.IsApkBackup(null));
    }

    [TestMethod]
    public void AllFilesAreApks_AcceptsBackupAndRejectsGenericArchives()
    {
        Assert.IsTrue(FileHelper.AllFilesAreApks(["com.myapp.apkbkp"]));
        Assert.IsTrue(FileHelper.AllFilesAreApks(["app.apk", "mod.apex", "game.apkbkp"]));
        Assert.IsFalse(FileHelper.AllFilesAreApks(["com.myapp.tar.gz"]));
        Assert.IsFalse(FileHelper.AllFilesAreApks(["backup.zip"]));
        Assert.IsFalse(FileHelper.AllFilesAreApks(["app.apk", "notes.txt"]));
    }

    [TestMethod]
    public void FilterInstallApkNames_DropsLibAndOat()
    {
        var names = AppBackupHelper.FilterInstallApkNames(
        [
            "base.apk",
            "split_config.arm64_v8a.apk",
            "lib",
            "oat",
            "base.apex",
            "readme.txt",
        ]);

        CollectionAssert.AreEqual(
            new[] { "base.apk", "split_config.arm64_v8a.apk", "base.apex" },
            names.ToArray());
    }

    [TestMethod]
    public void SplitBackupMembers_ApksAtRootAndObbAsPackageFolder()
    {
        var (apks, obb) = AppBackupHelper.SplitBackupMembers(
        [
            "base.apk",
            "split_config.arm64_v8a.apk",
            "com.myapp",
            "com.myapp/main.123.com.myapp.obb",
            "./base.apk",
        ]);

        CollectionAssert.AreEqual(new[] { "base.apk", "split_config.arm64_v8a.apk" }, apks.ToArray());
        CollectionAssert.AreEqual(new[] { "com.myapp" }, obb.ToArray());
    }

    [TestMethod]
    public void BuildCreateArchiveScript_UsesGzipAndTwoDirectoryGroups()
    {
        var script = AppBackupHelper.BuildCreateArchiveScript(
            "tar",
            "/data/local/tmp/guid.tar.gz",
            "/data/app/~~hash~~/com.myapp-xxx",
            ["base.apk", "split_config.apk"],
            "com.myapp");

        StringAssert.Contains(script, "tar -czf");
        StringAssert.Contains(script, "-C");
        StringAssert.Contains(script, OBB_ROOT);
        StringAssert.Contains(script, "base.apk");
        StringAssert.Contains(script, "com.myapp");

        var firstC = script.IndexOf("-C");
        var secondC = script.IndexOf("-C", firstC + 1);
        Assert.IsTrue(secondC > firstC);
        Assert.IsFalse(script.Contains(@"\~"));
        StringAssert.Contains(script, "/data/app/~~hash~~/com.myapp-xxx");
    }

    [TestMethod]
    public void BuildCreateArchiveScript_OmitsObbGroupWhenMissing()
    {
        var script = AppBackupHelper.BuildCreateArchiveScript(
            "tar",
            "/data/local/tmp/guid.tar.gz",
            "/data/app/com.myapp",
            ["base.apk"],
            null);

        Assert.IsFalse(script.Contains(OBB_ROOT));
        Assert.AreEqual(1, CountToken(script, "-C"));
    }

    [TestMethod]
    public void BuildCreateArchiveScript_EmptySources_UsesNullFileList()
    {
        var script = AppBackupHelper.BuildCreateArchiveScript(
            "tar",
            "/data/local/tmp/guid.tar.gz",
            "/data/app/com.myapp",
            [],
            null);

        StringAssert.Contains(script, "-czf");
        StringAssert.Contains(script, "-T /dev/null");
        Assert.AreEqual(0, CountToken(script, "-C"));
    }

    [TestMethod]
    public void WindowsBackupFileName_UsesLowercaseExtension()
    {
        Assert.AreEqual("com.myapp.apkbkp", AppBackupHelper.WindowsBackupFileName("com.myapp"));
    }

    [TestMethod]
    public void ObbDirectory_IsUnderSdcardAndroidObb()
    {
        Assert.AreEqual("/sdcard/Android/obb/com.myapp", AppBackupHelper.ObbDirectory("com.myapp"));
    }

    [TestMethod]
    public void BuildCreateArchiveScript_VerboseAddsV()
    {
        var script = AppBackupHelper.BuildCreateArchiveScript(
            "tar",
            "/data/local/tmp/guid.tar.gz",
            "/data/app/com.myapp",
            ["base.apk"],
            null,
            verbose: true);

        StringAssert.Contains(script, "tar -czf");
        StringAssert.Contains(script, " -v ");
        Assert.IsFalse(script.Contains("2>&1"));
    }

    [TestMethod]
    public void NormalizeTarVerboseMember_StripsDotSlashAndTrailingSlash()
    {
        Assert.AreEqual("base.apk", AppBackupHelper.NormalizeTarVerboseMember("base.apk"));
        Assert.AreEqual("com.myapp", AppBackupHelper.NormalizeTarVerboseMember("./com.myapp/"));
        Assert.AreEqual("com.myapp/main.obb", AppBackupHelper.NormalizeTarVerboseMember("com.myapp/main.obb"));
        Assert.AreEqual("", AppBackupHelper.NormalizeTarVerboseMember("  "));
    }

    [TestMethod]
    public void ArchiveVerboseProgress_Reaches100WhenTarFinishes()
    {
        var progress = new ArchiveVerboseProgress(new Dictionary<string, long>
        {
            ["base.apk"] = 100,
            ["split.apk"] = 100,
        });

        Assert.AreEqual(0, progress.Percentage);

        progress.OnVerboseLine("base.apk");
        Assert.AreEqual(0, progress.Percentage);
        Assert.AreEqual("base.apk", progress.CurrentMember);

        progress.OnVerboseLine("./split.apk");
        Assert.AreEqual(50, progress.Percentage);
        Assert.AreEqual("split.apk", progress.CurrentMember);

        progress.OnVerboseLine("tar: ignored");
        Assert.AreEqual(50, progress.Percentage);

        progress.Finish();
        Assert.AreEqual(100, progress.Percentage);
        Assert.AreEqual(200, progress.CompletedBytes);
    }

    [TestMethod]
    public void ArchiveVerboseProgress_TwoPhasesCountWholeArchiveTwice()
    {
        var progress = new ArchiveVerboseProgress(new Dictionary<string, long>
        {
            ["a.txt"] = 100,
            ["b.txt"] = 100,
        }, phases: 2);

        progress.OnVerboseLine("a.txt");
        progress.OnVerboseLine("b.txt");
        progress.BeginPhase();
        Assert.AreEqual(50, progress.Percentage);

        progress.OnVerboseLine("a.txt");
        progress.OnVerboseLine("./b.txt");
        progress.Finish();
        Assert.AreEqual(100, progress.Percentage);
        Assert.AreEqual(400, progress.CompletedBytes);
    }

    [TestMethod]
    public void NormalizeUnzipMember_StripsInflatingPrefix()
    {
        Assert.AreEqual("d1/d2/x.txt", ArchiveVerboseProgress.NormalizeUnzipMember("inflating: d1/d2/x.txt"));
        Assert.AreEqual("d1/d2/dir", ArchiveVerboseProgress.NormalizeUnzipMember("creating: d1/d2/dir/"));
        Assert.AreEqual("", ArchiveVerboseProgress.NormalizeUnzipMember("Archive: foo.zip"));
        Assert.AreEqual("plain.txt", ArchiveVerboseProgress.NormalizeUnzipMember("plain.txt"));
    }

    [TestMethod]
    public void ArchiveHelper_DoesNotTreatApkbkpAsTar()
    {
        Assert.AreEqual(ArchiveFamily.None, ArchiveHelper.GetFamily("com.myapp.apkbkp"));
        Assert.AreEqual(ArchiveFamily.None, ArchiveHelper.GetFamily("/sdcard/com.myapp.apkbkp"));
    }

    private static int CountToken(string script, string token)
    {
        var count = 0;
        for (var i = 0; (i = script.IndexOf(token, i, System.StringComparison.Ordinal)) >= 0; i += token.Length)
            count++;
        return count;
    }
}
