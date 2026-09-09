using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class DrivePollTests
{
    [TestMethod]
    public void ParseDrivePollOutput_ParsesDfSectionsAndCounts()
    {
        var us = AdbExplorerConst.ADB_UNIT_SEP;
        var fs = AdbExplorerConst.ADB_FIELD_SEP;

        var stdout =
            $"{us}ROOT{us}\n" +
            "Filesystem     1K-blocks    Used Available Use% Mounted on\n" +
            "/dev/root        2981888 1234567   1747321  42% /\n" +
            $"{fs}\n" +
            $"{us}SDCARD{us}\n" +
            "Filesystem     1K-blocks     Used Available Use% Mounted on\n" +
            "/dev/fuse     119926528 32123456  87790000  27% /storage/emulated/0\n" +
            $"{fs}\n" +
            $"{us}EXT{us}\n" +
            "/dev/block/vold/public:179,1  15633408  1024000  14609408   7% /storage/ABCD-1234\n" +
            $"{fs}\n" +
            $"{us}TEMP{us}\n" +
            "Filesystem     1K-blocks    Used Available Use% Mounted on\n" +
            "/dev/root        2981888 1234567   1747321  42% /data\n" +
            $"{fs}\n" +
            $"{us}PKG{us}\n" +
            "128\n" +
            $"{fs}\n" +
            $"{us}TRASH{us}\n" +
            "3\n" +
            $"{fs}\n" +
            $"{us}TRASH_EXISTS{us}\n" +
            "1\n" +
            $"{fs}\n" +
            $"{us}APK{us}\n" +
            "2\n" +
            $"{fs}\n";

        var result = ADBService.ParseDrivePollOutput(
            stdout,
            DeviceType.Local,
            countRecycle: true,
            countPackages: true,
            countInstallers: true);

        Assert.AreEqual(4, result.Drives.Count);
        Assert.AreEqual(AbstractDrive.DriveType.Root, result.Drives[0].Type);
        Assert.AreEqual("/", result.Drives[0].Path);
        Assert.AreEqual(AbstractDrive.DriveType.Internal, result.Drives[1].Type);
        Assert.AreEqual("/sdcard", result.Drives[1].Path);
        Assert.AreEqual(AbstractDrive.DriveType.Unknown, result.Drives[2].Type);
        Assert.AreEqual("/storage/ABCD-1234", result.Drives[2].Path);
        Assert.AreEqual(AbstractDrive.DriveType.Temp, result.Drives[3].Type);
        Assert.AreEqual(3L, result.RecycleCount);
        Assert.AreEqual(128UL, result.PackagesCount);
        Assert.AreEqual(2UL, result.InstallersCount);
    }

    [TestMethod]
    public void ParseDrivePollOutput_MissingTrashFolder_ReturnsMinusOne()
    {
        var us = AdbExplorerConst.ADB_UNIT_SEP;
        var fs = AdbExplorerConst.ADB_FIELD_SEP;

        var stdout =
            $"{us}ROOT{us}\n/dev/root 100 50 50 50% /\n{fs}\n" +
            $"{us}SDCARD{us}\n/dev/fuse 100 50 50 50% /storage/emulated/0\n{fs}\n" +
            $"{us}EXT{us}\n{fs}\n" +
            $"{us}TEMP{us}\n/dev/root 100 50 50 50% /data\n{fs}\n" +
            $"{us}TRASH{us}\n0\n{fs}\n" +
            $"{us}TRASH_EXISTS{us}\n0\n{fs}\n";

        var result = ADBService.ParseDrivePollOutput(
            stdout,
            DeviceType.Local,
            countRecycle: true,
            countPackages: false,
            countInstallers: false);

        Assert.AreEqual(-1L, result.RecycleCount);
        Assert.IsNull(result.PackagesCount);
        Assert.IsNull(result.InstallersCount);
    }

    [TestMethod]
    public void ParseMountDump_ClassifiesSdAndUsbAndCapturesFriendlyNames()
    {
        // Captured from `adb shell dumpsys mount` on an ASUS ZenFone with a microSD card and a USB/OTG drive attached.
        var stdout =
            "Disks:\n" +
            "  DiskInfo{disk:179,0}:\n" +
            "    flags=SD size=31914983424 label=SanDisk \n" +
            "    sysPath=/sys//devices/platform/soc/8804000.sdhci/mmc_host/mmc0/mmc0:aaaa/block/mmcblk0 \n" +
            "  DiskInfo{disk:8,112}:\n" +
            "    flags=USB size=-1 label=Generic \n" +
            "    sysPath=/sys//devices/platform/soc/a600000.ssusb/a600000.dwc3/xhci-hcd.0.auto/usb1/1-1/1-1.4/1-1.4:1.0/host1/target1:0:0/1:0:0:0/block/sdh \n" +
            "  DiskInfo{disk:8,144}:\n" +
            "    flags=USB size=15401484288 label=SanDisk \n" +
            "    sysPath=/sys//devices/platform/soc/a600000.ssusb/a600000.dwc3/xhci-hcd.0.auto/usb1/1-1/1-1.2/1-1.2:1.0/host2/target2:0:0/2:0:0:0/block/sdj \n" +
            "\n" +
            "Volumes:\n" +
            "  VolumeInfo{public:179,1}:\n" +
            "    type=PUBLIC diskId=disk:179,0 partGuid= mountFlags=VISIBLE mountUserId=0 state=MOUNTED \n" +
            "    fsType=exfat fsUuid=6ADC-56BE fsLabel=Micro SD \n" +
            "    path=/storage/6ADC-56BE internalPath=/mnt/media_rw/6ADC-56BE \n" +
            "  VolumeInfo{public:8,145}:\n" +
            "    type=PUBLIC diskId=disk:8,144 partGuid= mountFlags=VISIBLE mountUserId=0 state=MOUNTED \n" +
            "    fsType=vfat fsUuid=7411-9A9B fsLabel=USB storage \n" +
            "    path=/storage/7411-9A9B internalPath=/mnt/media_rw/7411-9A9B \n" +
            "  VolumeInfo{emulated;0}:\n" +
            "    type=EMULATED diskId=null partGuid= mountFlags=PRIMARY|VISIBLE mountUserId=0 state=MOUNTED \n" +
            "    fsType=null fsUuid=null fsLabel=null \n" +
            "    path=/storage/emulated internalPath=/data/media \n";

        var result = ADBService.ParseMountDump(stdout);

        Assert.AreEqual(2, result.Count);

        Assert.IsTrue(result.TryGetValue("/storage/6ADC-56BE", out var sd));
        Assert.AreEqual(AbstractDrive.DriveType.Expansion, sd.Type);
        Assert.AreEqual("SanDisk", sd.Manufacturer);
        Assert.AreEqual("Micro SD", sd.VolumeLabel);

        Assert.IsTrue(result.TryGetValue("/storage/7411-9A9B", out var usb));
        Assert.AreEqual(AbstractDrive.DriveType.External, usb.Type);
        Assert.AreEqual("SanDisk", usb.Manufacturer);
        Assert.AreEqual("USB storage", usb.VolumeLabel);
    }
}
