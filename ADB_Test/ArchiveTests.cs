using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Test;

[TestClass]
public class ArchiveCapabilityTests
{
    private const string DeviceId = "test-device";

    [TestInitialize]
    public void Setup()
    {
        ShellCommands.DeviceCommands.Clear();
        ArchivePath.ClearCachesForTests();
        ArchivePath.TestResolveIsRegularFile = (_, path) =>
            path != "/sdcard/folder.zip"
            && ArchiveHelper.GetFamily(FileHelper.GetFullName(path)) is not ArchiveFamily.None;
    }

    [TestCleanup]
    public void Cleanup()
    {
        ShellCommands.DeviceCommands.Clear();
        ArchivePath.TestResolveIsRegularFile = null;
        ArchivePath.ClearCachesForTests();
    }

    private static DeviceShellCommands MakeCommands(
        bool tarExists = false,
        bool unzipExists = false,
        bool zipExists = false,
        bool tarAppendSupported = false,
        bool tarToCommandSupported = false,
        bool tarToStdoutSupported = false,
        bool crc32Exists = false,
        bool md5SumExists = false) => new()
    {
        TarExists = tarExists,
        UnzipExists = unzipExists,
        ZipExists = zipExists,
        TarAppendSupported = tarAppendSupported,
        TarToCommandSupported = tarToCommandSupported,
        TarToStdoutSupported = tarToStdoutSupported,
        Crc32Exists = crc32Exists,
        Md5SumExists = md5SumExists,
        Crc32Command = crc32Exists ? "cksum -HNPL" : null,
        Md5SumCommand = md5SumExists ? "md5sum" : null,
    };

    [TestMethod]
    public void GetFamily_ClassifiesTarAndZipExtensions()
    {
        Assert.AreEqual(ArchiveFamily.Tar, ArchiveHelper.GetFamily("backup.tar"));
        Assert.AreEqual(ArchiveFamily.Tar, ArchiveHelper.GetFamily("backup.tar.gz"));
        Assert.AreEqual(ArchiveFamily.Tar, ArchiveHelper.GetFamily("backup.tgz"));
        Assert.AreEqual(ArchiveFamily.Zip, ArchiveHelper.GetFamily("archive.zip"));
        Assert.AreEqual(ArchiveFamily.Zip, ArchiveHelper.GetFamily("app.apk"));
        Assert.AreEqual(ArchiveFamily.Zip, ArchiveHelper.GetFamily("bundle.xapk"));
        Assert.AreEqual(ArchiveFamily.None, ArchiveHelper.GetFamily("notes.txt"));
        Assert.AreEqual(ArchiveFamily.None, ArchiveHelper.GetFamily("image.gz"));
        Assert.AreEqual(ArchiveFamily.None, ArchiveHelper.GetFamily("package.rar"));
    }

    [TestMethod]
    public void IsCompressedTar_DistinguishesPlainAndCompressed()
    {
        Assert.IsFalse(ArchiveHelper.IsCompressedTar("backup.tar"));
        Assert.IsTrue(ArchiveHelper.IsCompressedTar("backup.tar.gz"));
        Assert.IsTrue(ArchiveHelper.IsCompressedTar("backup.tgz"));
    }

    [TestMethod]
    public void CanBrowse_RequiresVerifiedCommands()
    {
        Assert.IsFalse(ArchiveHelper.CanBrowse("backup.tar", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(tarExists: true);
        Assert.IsTrue(ArchiveHelper.CanBrowse("backup.tar", DeviceId));
        Assert.IsFalse(ArchiveHelper.CanBrowse("archive.zip", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true);
        Assert.IsTrue(ArchiveHelper.CanBrowse("archive.zip", DeviceId));
        Assert.IsTrue(ArchiveHelper.CanBrowse("app.apk", DeviceId));
    }

    [TestMethod]
    public void CanModify_ZipRequiresZipAndUnzip()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true);
        Assert.IsFalse(ArchiveHelper.CanModify("archive.zip", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true, zipExists: true);
        Assert.IsTrue(ArchiveHelper.CanModify("archive.zip", DeviceId));
    }

    [TestMethod]
    public void CanModify_TarRequiresAppendSupportAndUncompressedArchive()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(tarExists: true, tarAppendSupported: true);
        Assert.AreEqual(Resources.S_ARCHIVE_CAN_MODIFY, ArchiveHelper.GetArchiveModificationTooltip("backup.tar", DeviceId));
        Assert.IsTrue(ArchiveHelper.CanModify("backup.tar", DeviceId));
        Assert.AreEqual(Resources.S_ARCHIVE_READ_ONLY, ArchiveHelper.GetArchiveModificationTooltip("backup.tar.gz", DeviceId));
        Assert.IsFalse(ArchiveHelper.CanModify("backup.tar.gz", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(tarExists: true, tarAppendSupported: false);
        Assert.AreEqual(Resources.S_ARCHIVE_READ_ONLY, ArchiveHelper.GetArchiveModificationTooltip("backup.tar", DeviceId));
        Assert.IsFalse(ArchiveHelper.CanModify("backup.tar", DeviceId));
    }

    [TestMethod]
    public void GetArchiveModificationTooltip_ZipDependsOnZipCommand()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true);
        Assert.AreEqual(Resources.S_ARCHIVE_READ_ONLY, ArchiveHelper.GetArchiveModificationTooltip("archive.zip", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true, zipExists: true);
        Assert.AreEqual(Resources.S_ARCHIVE_CAN_MODIFY, ArchiveHelper.GetArchiveModificationTooltip("archive.zip", DeviceId));
    }

    [TestMethod]
    public void CanNavigateIntoArchive_SkipsDirectoryNamedLikeArchive()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true);
        Assert.IsFalse(ArchiveHelper.CanNavigateIntoArchive("/sdcard/folder.zip", "folder.zip", DeviceId, isInsideArchive: false));
    }

    [TestMethod]
    public void IsModificationAllowedAt_ReadOnlyArchivePath_ReturnsFalse()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true);
        var path = ArchivePath.Join("/sdcard/archive.zip", "docs");

        Assert.IsFalse(ArchiveHelper.IsModificationAllowedAt(path, DeviceId));
        Assert.IsTrue(ArchiveHelper.IsModificationAllowedAt("/sdcard/Download", DeviceId));
    }

    [TestMethod]
    public void TarHelpSupportsAppend_ParsesCommonHelpFormats()
    {
        const string gnuHelp = "  -r, --append              append files to the end of an archive";
        const string busyboxHelp = " -r        Append files to archive";
        const string toyboxAndroidHelp = AndroidToyboxTarHelp;

        Assert.IsTrue(ShellCommands.TarHelpSupportsAppend(gnuHelp));
        Assert.IsTrue(ShellCommands.TarHelpSupportsAppend(busyboxHelp));
        Assert.IsFalse(ShellCommands.TarHelpSupportsAppend(toyboxAndroidHelp));
        Assert.IsFalse(ShellCommands.TarHelpSupportsAppend("List files in archive"));
    }

    [TestMethod]
    public void TarHelpSupportsToCommand_ParsesHelp()
    {
        Assert.IsTrue(ShellCommands.TarHelpSupportsToCommand("  --to-command=COMMAND   pipe extracted files to shell COMMAND"));
        Assert.IsTrue(ShellCommands.TarHelpSupportsToCommand(":(to-command):~(strip-components)"));
        Assert.IsFalse(ShellCommands.TarHelpSupportsToCommand("  -r, --append              append files"));
        Assert.IsFalse(ShellCommands.TarHelpSupportsToCommand(AndroidToyboxTarHelp));
    }

    [TestMethod]
    public void TarHelpSupportsToStdout_ParsesToyboxAndGnu()
    {
        const string gnuHelp = "  -O, --to-stdout            extract files to standard output";
        const string toyboxOpts = "O(to-stdout)p(same-permissions)";

        Assert.IsTrue(ShellCommands.TarHelpSupportsToStdout(AndroidToyboxTarHelp));
        Assert.IsTrue(ShellCommands.TarHelpSupportsToStdout(gnuHelp));
        Assert.IsTrue(ShellCommands.TarHelpSupportsToStdout(toyboxOpts));
        Assert.IsFalse(ShellCommands.TarHelpSupportsToStdout("  -r, --append              append files"));
    }

    [TestMethod]
    public void ParseArchiveProbeOutput_ReadsFlagsAndTarHelp()
    {
        var us = AdbExplorerConst.ADB_UNIT_SEP;
        var fs = AdbExplorerConst.ADB_FIELD_SEP;
        var stdout = $"{us}TAR{us}  -r, --append              append files to the end of an archive\n  --to-command=COMMAND{fs}{us}UNZIP{us}{fs}{us}ZIP{us}Copyright (c) 1990-2008 Info-ZIP";

        var result = ShellCommands.ParseArchiveProbeOutput(stdout);

        Assert.IsTrue(result.TarExists);
        Assert.IsFalse(result.UnzipExists);
        Assert.IsTrue(result.ZipExists);
        Assert.IsTrue(result.TarAppendSupported);
        Assert.IsTrue(result.TarToCommandSupported);
        Assert.IsFalse(result.TarToStdoutSupported);
    }

    [TestMethod]
    public void ParseArchiveProbeOutput_ReadsAndroidToyboxTarHelp()
    {
        var us = AdbExplorerConst.ADB_UNIT_SEP;
        var fs = AdbExplorerConst.ADB_FIELD_SEP;
        var stdout = $"{us}TAR{us}{AndroidToyboxTarHelp}{fs}{us}UNZIP{us}{fs}{us}ZIP{us}";

        var result = ShellCommands.ParseArchiveProbeOutput(stdout);

        Assert.IsTrue(result.TarExists);
        Assert.IsFalse(result.TarAppendSupported);
        Assert.IsFalse(result.TarToCommandSupported);
        Assert.IsTrue(result.TarToStdoutSupported);
    }

    /// <summary>Android toybox 0.8.12 <c>tar --help</c> (no append / --to-command; has <c>O Extract to stdout</c>).</summary>
    private const string AndroidToyboxTarHelp = """
        Toybox 0.8.12-android multicall binary (see toybox --help)

        usage: tar [-cxt] [-fvohmjkOS] [-XTCf NAME] [--selinux] [FILE...]

        Create, extract, or list files in a .tar (or compressed t?z) file.

        Options:
        c  Create                x  Extract               t  Test (list)
        f  tar FILE (default -)  C  Change to DIR first   v  Verbose display
        J  xz compression        j  bzip2 compression     z  gzip compression
        o  Ignore owner          h  Follow symlinks       m  Ignore mtime
        O  Extract to stdout     X  exclude names in FILE T  include names in FILE
        s  Sort dirs (--sort)    Z  zstd compression

        --exclude        FILENAME to exclude  --full-time         Show seconds with -tv
        --mode MODE      Adjust permissions   --owner NAME[:UID]  Set file ownership
        --mtime TIME     Override timestamps  --group NAME[:GID]  Set file group
        --sparse         Record sparse files  --selinux           Save/restore labels
        --restrict       All under one dir    --no-recursion      Skip dir contents
        --numeric-owner  Use numeric uid/gid, not user/group names
        --null           Filenames in -T FILE are null-separated, not newline
        --strip-components NUM  Ignore first NUM directory components when extracting
        --xform=SED      Modify filenames via SED expression (ala s/find/replace/g)
        -I PROG          Filter through PROG to compress or PROG -d to decompress
        """;

    [TestMethod]
    public void SupportsHashValidation_PrefersCrc32ThenMd5()
    {
        // Tar needs a hash pipeline (--to-command or -O) plus a hash tool.
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(tarExists: true, tarToCommandSupported: true);
        Assert.IsFalse(ArchiveHelper.SupportsHashValidation("backup.tar.gz", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(
            tarExists: true, tarToCommandSupported: true, md5SumExists: true);
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("backup.tar.gz", DeviceId));
        Assert.IsFalse(ArchiveHelper.UsesCrc32Validation("backup.tar", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(
            tarExists: true, tarToStdoutSupported: true, md5SumExists: true);
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("backup.tar.gz", DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(
            tarExists: true, tarToCommandSupported: true, crc32Exists: true, md5SumExists: true);
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("backup.tar.gz", DeviceId));
        Assert.IsTrue(ArchiveHelper.UsesCrc32Validation("backup.tar", DeviceId));

        // Zip TOC is CRC-32: Android dest needs shell cksum; Windows pull can CRC locally.
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true, md5SumExists: true);
        Assert.IsFalse(ArchiveHelper.SupportsHashValidation("archive.zip", DeviceId, androidDestination: true));
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("archive.zip", DeviceId, androidDestination: false));
        Assert.IsTrue(ArchiveHelper.UsesCrc32Validation("archive.zip", DeviceId, androidDestination: false));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(unzipExists: true, crc32Exists: true);
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("archive.zip", DeviceId));
        Assert.IsTrue(ArchiveHelper.SupportsHashValidation("app.apk", DeviceId));
        Assert.IsTrue(ArchiveHelper.UsesCrc32Validation("archive.zip", DeviceId));
    }

    [TestMethod]
    public void ParseHashProbeOutput_ReadsCksumAndMd5()
    {
        var us = AdbExplorerConst.ADB_UNIT_SEP;
        var fs = AdbExplorerConst.ADB_FIELD_SEP;

        var withBoth = $"{us}CKSUM{us}usage: cksum [-HIPLN]{fs}{us}MD5{us}usage: md5sum [-bcs]";
        var both = ShellCommands.ParseHashProbeOutput(withBoth);
        Assert.IsTrue(both.Crc32Exists);
        Assert.AreEqual("cksum -HNPL", both.Crc32Command);
        Assert.IsTrue(both.Md5SumExists);
        Assert.AreEqual("md5sum", both.Md5SumCommand);

        var cksumOnly = $"{us}CKSUM{us}usage: cksum{fs}{us}MD5{us}";
        var cksum = ShellCommands.ParseHashProbeOutput(cksumOnly, busyBoxExists: true);
        Assert.IsTrue(cksum.Crc32Exists);
        Assert.AreEqual("busybox cksum -HNPL", cksum.Crc32Command);
        Assert.IsFalse(cksum.Md5SumExists);

        var md5Only = $"{us}CKSUM{us}{fs}{us}MD5{us}usage: md5sum";
        var md5 = ShellCommands.ParseHashProbeOutput(md5Only);
        Assert.IsFalse(md5.Crc32Exists);
        Assert.IsTrue(md5.Md5SumExists);
        Assert.AreEqual("md5sum", md5.Md5SumCommand);
    }

    [TestMethod]
    public void GetValidationHashMode_PrefersCrc32OverMd5()
    {
        ShellCommands.DeviceCommands[DeviceId] = MakeCommands();
        Assert.AreEqual(ValidationHashMode.None, ShellCommands.GetValidationHashMode(DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(md5SumExists: true);
        Assert.AreEqual(ValidationHashMode.Md5, ShellCommands.GetValidationHashMode(DeviceId));

        ShellCommands.DeviceCommands[DeviceId] = MakeCommands(crc32Exists: true, md5SumExists: true);
        Assert.AreEqual(ValidationHashMode.Crc32, ShellCommands.GetValidationHashMode(DeviceId));
    }
}

[TestClass]
public class ArchivePathTests
{
    [TestInitialize]
    public void InitArchivePathTests()
    {
        ArchivePath.ClearCachesForTests();
        ArchivePath.TestResolveIsRegularFile = (_, _) => true;
    }

    [TestCleanup]
    public void CleanupArchivePathTests()
    {
        ArchivePath.TestResolveIsRegularFile = null;
        ArchivePath.ClearCachesForTests();
    }

        [TestMethod]
        public void IsArchivePath_TreatsFolderZipSubdirAsDeviceFolder()
        {
            ArchivePath.TestResolveIsRegularFile = (_, path) => path == "/sdcard/folder.zip" ? false : true;

            Assert.IsFalse(ArchivePath.IsArchivePath("/sdcard/folder.zip/subdir", "device"));
        }

        [TestMethod]
        public void TryParse_SkipsDirectoryNamedLikeArchive()
    {
        ArchivePath.TestResolveIsRegularFile = (_, path) => path == "/sdcard/folder.zip" ? false : true;

        Assert.IsFalse(ArchivePath.TryParse("/sdcard/folder.zip/readme.txt", out _, out _));
        Assert.IsTrue(ArchivePath.TryParse("/sdcard/folder.zip/backup.zip/docs/readme.txt", out var archive, out var inner));
        Assert.AreEqual("/sdcard/folder.zip/backup.zip", archive);
        Assert.AreEqual("docs/readme.txt", inner);
    }

    [TestMethod]
    public void TryParse_SplitsArchiveAndInternalPath()
    {
        Assert.IsFalse(ArchivePath.TryParse("/sdcard/backup.tar.gz", out _, out _));
        Assert.IsTrue(ArchivePath.TryParse("/sdcard/backup.tar.gz/docs/readme.txt", out var archive, out var inner));
        Assert.AreEqual("/sdcard/backup.tar.gz", archive);
        Assert.AreEqual("docs/readme.txt", inner);
    }

    [TestMethod]
    public void Join_AndGetParent_WalkArchiveHierarchy()
    {
        var root = ArchivePath.Join("/sdcard/backup.zip", "");
        Assert.AreEqual("/sdcard/backup.zip/", root);
        Assert.AreEqual("/sdcard", ArchivePath.GetParent(root));

        var nested = ArchivePath.Join("/sdcard/backup.zip", "docs/readme.txt");
        Assert.AreEqual("/sdcard/backup.zip/docs", ArchivePath.GetParent(nested));
    }

    [TestMethod]
    public void GetFileStats_GroupsNestedTarEntries()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", false, 10, null),
            new("docs/images/pic.png", false, 20, null),
            new("root.txt", false, 5, null),
        };

        var children = ArchiveListing.GetFileStats("/sdcard/a.zip", "", entries).Select(c => c.FullName).ToList();
        CollectionAssert.AreEquivalent(new[] { "docs", "root.txt" }, children);
        Assert.IsTrue(ArchiveListing.GetFileStats("/sdcard/a.zip", "", entries).First(c => c.FullName == "docs").Type is FileType.Folder);
    }

    [TestMethod]
    public void GetFileStats_SkipsDirectoryMarkerInsideFolder()
    {
        var entries = new List<ArchiveEntry>
        {
            new("folder/", true, 0, null),
            new("folder/file.txt", false, 10, null),
        };

        var inside = ArchiveListing.GetFileStats("/sdcard/a.zip", "folder", entries).Select(f => f.FullName).ToList();
        CollectionAssert.AreEquivalent(new[] { "file.txt" }, inside);

        var root = ArchiveListing.GetFileStats("/sdcard/a.zip", "", entries).Select(f => f.FullName).ToList();
        CollectionAssert.AreEquivalent(new[] { "folder" }, root);
    }

    [TestMethod]
    public void SeparatePath_ArchiveRoot_IsSingleDashedCrumbForZip()
    {
        Data.CurrentDisplayNames["/sdcard"] = "Internal";

        var driveView = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        var crumbs = NavigationBox.SeparatePath($"{driveView}/sdcard/Download/app.zip/WpfApp2/WpfApp2")
            .Select(l => l.Path)
            .ToList();

        CollectionAssert.DoesNotContain(crumbs, "/sdcard/Download/app.zip");
        Assert.IsTrue(crumbs.Contains("/sdcard/Download/app.zip/"));
        Assert.IsTrue(crumbs.Contains("/sdcard/Download/app.zip/WpfApp2/WpfApp2"));
    }

    [TestMethod]
    public void ParseTar_ReadsPermissions()
    {
        const string stdout = "-rw-r--r-- root/root     4408 1989-10-02 14:45 file.c\n";

        var entries = ArchiveListing.ParseListing(stdout, ArchiveFamily.Tar);

        Assert.HasCount(1, entries);
        Assert.IsTrue(entries[0].Permissions.HasValue);
        var expected = System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.OtherRead;
        Assert.AreEqual(expected, entries[0].Permissions.Value);

        var stat = ArchiveListing.GetFileStats("/sdcard/a.tar", "", entries).First();
        Assert.AreEqual(expected, stat.Permissions);
    }

    [TestMethod]
    public void CanNavigateIntoArchive_BlocksNestedArchives()
    {
        const string DeviceId = "device";

        Assert.IsFalse(ArchiveHelper.CanNavigateIntoArchive("/sdcard/app.zip/nested.zip", "nested.zip", DeviceId, isInsideArchive: true));
        Assert.IsFalse(ArchiveHelper.CanNavigateIntoArchive("/sdcard/app.zip/nested.zip", "nested.zip", DeviceId, isInsideArchive: false));
    }

    [TestMethod]
    public void FormatDetailsLocation_ReplacesArchiveSeparatorWithArrow()
    {
        Assert.AreEqual("/sdcard/app.zip", ArchivePath.FormatDetailsLocation("/sdcard/app.zip/"));
        Assert.AreEqual("/sdcard/app.zip\n→ inner/path", ArchivePath.FormatDetailsLocation("/sdcard/app.zip/inner/path"));
    }

    [TestMethod]
    public void ParseZipVerbose_ReadsEntriesSummaryAndMetadata()
    {
        const string stdout = """
            Archive:  app.zip
             Length   Method    Size  Cmpr    Date    Time   CRC-32   Name
            --------  ------  ------- ---- ---------- ----- --------  ----
                4408  Defl:N    1382  69% 1989-10-02 14:45 5a73bcd7  file.c
                2771  Defl:N     934  66% 1989-10-02 15:17 694a1f6c  file.h
            --------          -------  ---
                7189              2316  68%                            2 files
            """;

        var toc = ArchiveListing.ParseToc(stdout, ArchiveFamily.Zip);

        Assert.AreEqual(2, toc.Entries.Count);
        Assert.IsTrue(toc.Summary.HasValue);
        var summary = toc.Summary.Value;
        Assert.AreEqual(7189, summary.UncompressedSize);
        Assert.AreEqual(2316, summary.CompressedSize);
        Assert.AreEqual("68%", summary.Ratio);
        Assert.AreEqual(2, summary.FileCount);

        var file = toc.Entries.First(e => e.Path == "file.c");
        Assert.AreEqual(4408, file.Size);
        Assert.AreEqual(1382, file.CompressedSize);
        Assert.AreEqual("Defl:N", file.Method);
        Assert.AreEqual("69%", file.Ratio);
        Assert.AreEqual("5a73bcd7", file.Crc);

        var stats = ArchiveListing.GetFileStats("/sdcard/app.zip", "", toc.Entries).ToList();
        var stat = stats.First(s => s.FullName == "file.c");
        Assert.AreEqual(1382, stat.CompressedSize);
        Assert.AreEqual("Defl:N", stat.CompressionMethod);
        Assert.AreEqual("Deflate (normal)", ArchiveHelper.GetZipMethodDisplayName(stat.CompressionMethod!));
    }

    [TestMethod]
    public void GetZipMethodDisplayName_MapsInfoZipCodes()
    {
        Assert.AreEqual("Deflate (normal)", ArchiveHelper.GetZipMethodDisplayName("Defl:N"));
        Assert.AreEqual("Deflate (maximum)", ArchiveHelper.GetZipMethodDisplayName("Defl:X"));
        Assert.AreEqual("Deflate (fast)", ArchiveHelper.GetZipMethodDisplayName("Defl:F"));
        Assert.AreEqual("Deflate (super fast)", ArchiveHelper.GetZipMethodDisplayName("Defl:S"));
        Assert.AreEqual("Deflate (normal)", ArchiveHelper.GetZipMethodDisplayName("defN"));
        Assert.AreEqual("Stored", ArchiveHelper.GetZipMethodDisplayName("Stor"));
        Assert.AreEqual("Stored", ArchiveHelper.GetZipMethodDisplayName("Stored"));
        Assert.AreEqual("Reduced (level 2)", ArchiveHelper.GetZipMethodDisplayName("Re:2"));
        Assert.AreEqual("Shrunk", ArchiveHelper.GetZipMethodDisplayName("Shrk"));
    }

    [TestMethod]
    public void GetOutputName_UsesBasenameOnly()
    {
        Assert.AreEqual("readme.txt", ArchiveExtract.GetOutputName("docs/readme.txt"));
        Assert.AreEqual("images", ArchiveExtract.GetOutputName("docs/images"));
        Assert.AreEqual("file.c", ArchiveExtract.GetOutputName("file.c"));
    }

    [TestMethod]
    public void GetMemberPathsToExtract_FileIsSingleMember()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", false, 10, null),
            new("docs/images/pic.png", false, 20, null),
        };

        var members = ArchiveExtract.GetMemberPathsToExtract(entries, "docs/readme.txt", isDirectory: false);
        CollectionAssert.AreEqual(new[] { "docs/readme.txt" }, members.ToList());
    }

    [TestMethod]
    public void GetMemberPathsToExtract_DirectoryIncludesDescendants()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs", true, 0, null),
            new("docs/readme.txt", false, 10, null),
            new("docs/images/pic.png", false, 20, null),
            new("other.txt", false, 5, null),
        };

        var members = ArchiveExtract.GetMemberPathsToExtract(entries, "docs", isDirectory: true).ToList();
        CollectionAssert.Contains(members, "docs");
        CollectionAssert.Contains(members, "docs/readme.txt");
        CollectionAssert.Contains(members, "docs/images/pic.png");
        CollectionAssert.DoesNotContain(members, "other.txt");
    }

    [TestMethod]
    public void BuildFolderTreeFromEntries_MapsDescendantsUnderExtractedRoot()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", false, 10, null),
            new("docs/images/pic.png", false, 20, null),
            new("other.txt", false, 5, null),
        };

        var tree = ArchiveExtract.BuildFolderTreeFromEntries(
            "docs",
            "/data/local/tmp/out/docs",
            entries);

        var names = tree.Select(t => t.Name).ToList();
        CollectionAssert.Contains(names, "/data/local/tmp/out/docs/readme.txt");
        CollectionAssert.Contains(names, "/data/local/tmp/out/docs/images");
        CollectionAssert.Contains(names, "/data/local/tmp/out/docs/images/pic.png");
        CollectionAssert.DoesNotContain(names, "/data/local/tmp/out/docs/other.txt");

        Assert.IsTrue(tree.First(t => t.Name.EndsWith("/images")).IsFolder);
        Assert.IsFalse(tree.First(t => t.Name.EndsWith("pic.png")).IsFolder);
    }

    [TestMethod]
    public void SyncFile_GetFolderTree_TreatsImplicitNestedDirsAsFolders()
    {
        // TOC-style listing without an explicit directory marker for "images".
        FolderTree[] tree =
        [
            new("/tmp/out/docs/readme.txt", 10, 0),
            new("/tmp/out/docs/images/pic.png", 20, 0),
        ];

        var sync = new SyncFile(new FileClass("docs", "/tmp/out/docs", FileType.Folder), tree);
        var images = sync.Children.FirstOrDefault(c => c.FullName == "images");

        Assert.IsNotNull(images);
        Assert.IsTrue(images.IsDirectory);
        Assert.AreEqual(1, images.Children.Count);
        Assert.AreEqual("pic.png", images.Children[0].FullName);
        Assert.IsFalse(images.Children[0].IsDirectory);
    }

    [TestMethod]
    public void GetZipCrc32FromEntries_RelativizesToSelection()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", false, 10, null, Crc: "48d7f063"),
            new("docs/images/pic.png", false, 20, null, Crc: "aabbccdd"),
            new("other.txt", false, 5, null, Crc: "11223344"),
        };

        var file = Security.GetZipCrc32FromEntries(entries, "docs/readme.txt", isDirectory: false);
        Assert.AreEqual(1, file.Count);
        Assert.AreEqual("48D7F063", file["readme.txt"]);

        var folder = Security.GetZipCrc32FromEntries(entries, "docs", isDirectory: true);
        Assert.AreEqual(2, folder.Count);
        Assert.AreEqual("48D7F063", folder["readme.txt"]);
        Assert.AreEqual("AABBCCDD", folder["images/pic.png"]);
        Assert.IsFalse(folder.ContainsKey("other.txt"));
    }
}
