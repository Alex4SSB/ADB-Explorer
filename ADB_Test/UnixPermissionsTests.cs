using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ADB_Test;

[TestClass]
public class UnixPermissionsTests
{
    [TestMethod]
    public void ParsePasswdAndGroupNamesTest()
    {
        var passwd = """
            root:x:0:0:root:/:/system/bin/sh
            # comment
            shell:x:2000:2000:shell:/data:/system/bin/sh
            media_rw:x:1023:1023:media_rw:/data:/system/bin/sh
            """;
        var names = ShellAccessHelper.ParsePasswdNames(passwd).ToArray();
        CollectionAssert.AreEqual(new[] { "root", "shell", "media_rw" }, names);

        var group = """
            root:x:0:root
            sdcard_rw:x:1015:shell
            """;
        var groups = ShellAccessHelper.ParseGroupNames(group).ToArray();
        CollectionAssert.AreEqual(new[] { "root", "sdcard_rw" }, groups);

        Assert.IsFalse(ShellAccessHelper.ParsePasswdNames("").Any());
        Assert.IsFalse(ShellAccessHelper.ParsePasswdNames(null).Any());

        var concatenated = """
            root:x:0:0:root:/:/system/bin/sh
            vendor_foo:x:2901:2901::/:/system/bin/sh
            """;
        var concatNames = ShellAccessHelper.ParsePasswdNames(concatenated).ToArray();
        CollectionAssert.AreEqual(new[] { "root", "vendor_foo" }, concatNames);
    }

    [TestMethod]
    public void AndroidAids_ContainsPlatformNames()
    {
        CollectionAssert.Contains(AndroidAids.Names, "root");
        CollectionAssert.Contains(AndroidAids.Names, "shell");
        CollectionAssert.Contains(AndroidAids.Names, "media_rw");
        CollectionAssert.Contains(AndroidAids.Names, "sdcard_rw");
        CollectionAssert.Contains(AndroidAids.Names, "system");
        CollectionAssert.Contains(AndroidAids.Names, "nobody");
        CollectionAssert.Contains(AndroidAids.Names, "mediadrm");
        CollectionAssert.Contains(AndroidAids.Names, "mediaex");
        CollectionAssert.Contains(AndroidAids.Names, "mediacodec");

        CollectionAssert.DoesNotContain(AndroidAids.Names, "unused1");
        CollectionAssert.DoesNotContain(AndroidAids.Names, "media_drm");
        CollectionAssert.DoesNotContain(AndroidAids.Names, "oem_reserved_start");

        CollectionAssert.Contains(AndroidAids.PasswdPaths, "/etc/passwd");
        CollectionAssert.Contains(AndroidAids.GroupPaths, "/etc/group");
        CollectionAssert.Contains(AndroidAids.PasswdPaths, "/vendor/etc/passwd");
    }

    [TestMethod]
    public void CombineKnownIdentities_EmptyStdoutStillHasAids()
    {
        var fromEmpty = ShellAccessHelper.CombineKnownIdentities("");
        var fromNull = ShellAccessHelper.CombineKnownIdentities(null);

        CollectionAssert.Contains(fromEmpty.ToArray(), "root");
        CollectionAssert.Contains(fromEmpty.ToArray(), "shell");
        CollectionAssert.Contains(fromEmpty.ToArray(), "media_rw");
        CollectionAssert.Contains(fromNull.ToArray(), "sdcard_rw");
    }

    [TestMethod]
    public void CombineKnownIdentities_MergesColonFileNames()
    {
        var merged = ShellAccessHelper.CombineKnownIdentities("""
            vendor_foo:x:2901:2901::/:/system/bin/sh
            # comment
            oem_bar:x:2902:2902::/:/system/bin/sh
            """).ToArray();

        CollectionAssert.Contains(merged, "root");
        CollectionAssert.Contains(merged, "vendor_foo");
        CollectionAssert.Contains(merged, "oem_bar");
    }

    [TestMethod]
    public void ComposeModeAndChmodOctalTest()
    {
        var mode = ShellAccessHelper.ComposeMode(
            userRead: true, userWrite: true, userExecute: true,
            groupRead: true, groupWrite: false, groupExecute: true,
            otherRead: true, otherWrite: false, otherExecute: true);
        Assert.AreEqual("755", ShellAccessHelper.ToChmodOctal(mode));

        var writeOnly = ShellAccessHelper.ComposeMode(
            false, true, false,
            false, false, false,
            false, false, false);
        Assert.AreEqual("200", ShellAccessHelper.ToChmodOctal(writeOnly));
    }

    [TestMethod]
    public void GetAllowedUnixChangesTest()
    {
        var shell = new ShellIdentity("shell", 2000, 2000, new HashSet<int> { 2000, 1015 });
        var root = new ShellIdentity("root", 0, 0, new HashSet<int> { 0 });

        var asRoot = ShellAccessHelper.GetAllowedChanges(1023, "media_rw", root);
        Assert.IsTrue(asRoot.Mode && asRoot.Owner && asRoot.Group);

        var asOwner = ShellAccessHelper.GetAllowedChanges(2000, "shell", shell);
        Assert.IsTrue(asOwner.Mode);
        Assert.IsFalse(asOwner.Owner);
        Assert.IsTrue(asOwner.Group);

        var asOwnerByName = ShellAccessHelper.GetAllowedChanges(null, "shell", shell);
        Assert.IsTrue(asOwnerByName.Mode);
        Assert.IsFalse(asOwnerByName.Owner);

        var asOther = ShellAccessHelper.GetAllowedChanges(1023, "media_rw", shell);
        Assert.IsFalse(asOther.Any);

        Assert.IsFalse(ShellAccessHelper.GetAllowedChanges(2000, "shell", null).Any);
    }

    [TestMethod]
    public void SupportsUnixMetadataChanges_RejectsEmptyPath()
    {
        Assert.IsFalse(DriveHelper.SupportsUnixMetadataChanges("", null));
        Assert.IsFalse(DriveHelper.SupportsUnixMetadataChanges(null, null));
    }

    [TestMethod]
    public void PermissionLetterConverterTest()
    {
        var converter = new PermissionLetterConverter();
        Assert.AreEqual("r", converter.Convert(true, typeof(string), "r", CultureInfo.InvariantCulture));
        Assert.AreEqual("-", converter.Convert(false, typeof(string), "r", CultureInfo.InvariantCulture));
        Assert.AreEqual("-", converter.Convert(null, typeof(string), "x", CultureInfo.InvariantCulture));
    }
}
