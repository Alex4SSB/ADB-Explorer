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
}
