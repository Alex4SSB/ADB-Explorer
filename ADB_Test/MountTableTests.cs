using ADB_Explorer.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class MountTableTests
{
    private const string Pixel3XlStyleMount = """
        /dev/root on / type ext4 (ro,seclabel,noatime)
        proc on /proc type proc (rw,relatime)
        tmpfs on /dev type tmpfs (rw,seclabel,nosuid,relatime,mode=755)
        /dev/block/dm-4 on /vendor type ext4 (ro,seclabel,noatime)
        overlay on /vendor type overlay (rw,seclabel,noatime,lowerdir=/vendor,upperdir=/mnt/scratch/overlay/vendor/upper,workdir=/mnt/scratch/overlay/vendor/work)
        /data/media on /mnt/runtime/write/emulated type sdcardfs (rw,nosuid,nodev,noexec,noatime)
        /mnt/pass_through/0/emulated on /storage/emulated type overlay (rw,nosuid,nodev,noexec,noatime)
        """;

    [TestMethod]
    public void Parse_ReadsMountPointsAndOptions()
    {
        var table = MountTable.Parse(Pixel3XlStyleMount);

        Assert.AreEqual(7, table.Entries.Count);
        Assert.AreEqual("/dev/root", table.Entries[0].BlockDev);
        Assert.AreEqual("/", table.Entries[0].MountPoint);
        Assert.AreEqual("ext4", table.Entries[0].FileSystemType);
        CollectionAssert.Contains(table.Entries[0].Options, "ro");
    }

    [TestMethod]
    public void Find_VendorOverlayWinsOverLowerAndRoot()
    {
        var table = MountTable.Parse(Pixel3XlStyleMount);

        var vendor = table.Find("/vendor/manifest.xml");
        Assert.IsNotNull(vendor);
        Assert.AreEqual("/vendor", vendor.Value.MountPoint);
        Assert.AreEqual("overlay", vendor.Value.FileSystemType);
        Assert.IsFalse(DriveRestrictions.From(vendor.Value.Options).ReadOnly);
    }

    [TestMethod]
    public void Find_RootStaysReadOnly()
    {
        var table = MountTable.Parse(Pixel3XlStyleMount);

        var root = table.Find("/");
        Assert.IsNotNull(root);
        Assert.AreEqual("/", root.Value.MountPoint);
        Assert.IsTrue(DriveRestrictions.From(root.Value.Options).ReadOnly);

        var systemFile = table.Find("/system/build.prop");
        Assert.IsNotNull(systemFile);
        Assert.AreEqual("/", systemFile.Value.MountPoint);
        Assert.IsTrue(DriveRestrictions.From(systemFile.Value.Options).ReadOnly);
    }

    [TestMethod]
    public void Find_DoesNotTreatVendorAsPrefixOfVendor2()
    {
        var table = MountTable.Parse("""
            /dev/root on / type ext4 (ro)
            overlay on /vendor type overlay (rw)
            """);

        var other = table.Find("/vendor2/lib");
        Assert.IsNotNull(other);
        Assert.AreEqual("/", other.Value.MountPoint);
        Assert.IsTrue(DriveRestrictions.From(other.Value.Options).ReadOnly);
    }

    [TestMethod]
    public void Find_IncludeRootFalse_SkipsRootFilesystem()
    {
        var table = MountTable.Parse(Pixel3XlStyleMount);

        Assert.IsNull(table.Find("/system/bin", includeRoot: false));

        var emulated = table.Find("/storage/emulated/0/Download", includeRoot: false);
        Assert.IsNotNull(emulated);
        Assert.AreEqual("/storage/emulated", emulated.Value.MountPoint);
        Assert.IsFalse(DriveRestrictions.From(emulated.Value.Options).ReadOnly);
        Assert.IsTrue(DriveRestrictions.From(emulated.Value.Options).NoExec);
    }

    [TestMethod]
    public void Covers_RootAndNestedMounts()
    {
        Assert.IsTrue(MountTable.Covers("/", "/vendor/manifest.xml"));
        Assert.IsTrue(MountTable.Covers("/vendor", "/vendor"));
        Assert.IsTrue(MountTable.Covers("/vendor", "/vendor/manifest.xml"));
        Assert.IsFalse(MountTable.Covers("/vendor", "/vendor2"));
        Assert.IsFalse(MountTable.Covers("/vendor", "/"));
    }

    [TestMethod]
    public void Parse_Empty_IsEmptyTable()
    {
        Assert.IsTrue(MountTable.Parse(null).IsEmpty);
        Assert.IsTrue(MountTable.Parse("").IsEmpty);
        Assert.IsNull(MountTable.Empty.Find("/vendor"));
    }
}
