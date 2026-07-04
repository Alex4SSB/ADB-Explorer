using ADB_Explorer.Controls;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Strings;
using ADB_Explorer.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ADB_Test
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public void ToSizeTest()
        {
            Data.Settings = new AppSettings { UILanguage = "en-US" };

            var testVals = new Dictionary<long, string>()
            {
                { 0, "0B" },
                { 300, "300B" },
                { 33000, "32.2KB" }, // 32.226
                { 500690, "489KB" }, // 488.955
                { 1024204, "1MB" }, // 1.0002
                { 1200100, "1.1MB" }, // 1.145
                { 3400200100, "3.2GB" }, // 1.667
                { 1200300400500, "1,117.9GB" } // 1117.86
            };

            foreach (var item in testVals)
            {
                string v = item.Key.BytesToSize();
                Assert.IsTrue(v == item.Value);
            }

        }

        [TestMethod]
        public void ToDriveSizeTest()
        {
            Data.Settings = new AppSettings { UILanguage = "en-US" };

            Assert.AreEqual("32 GB", (32L * 1024 * 1024 * 1024).BytesToDriveSize(true));
            Assert.AreEqual("1,117.9 GB", 1200300400500L.BytesToDriveSize(true));
        }

        [TestMethod]
        public void ToTimeTest()
        {
            Assert.AreEqual("50ms", UnitConverter.ToTime(.05));
            Assert.AreEqual("1:00:00h", UnitConverter.ToTime(3600));
        }

        [TestMethod]
        public void TextBoxValidationTest1()
        {
            var caretIndex = 1;
            var text = "0";
            var altText = "0";
            var maxLength = -1;

            for (int i = 1; i < 6; i++)
            {
                text += i;
                caretIndex++;

                TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, separator: '-', maxChars: 6);

                var index = i - 1 + (i - 1);
                Assert.AreEqual($"{i - 1}-{i}", text[index..(index + 3)]);
                Assert.AreEqual(text.Length, caretIndex);
                Assert.AreEqual(text, altText);
            }
            Assert.AreEqual(11, maxLength);
        }

        [TestMethod]
        public void TextBoxValidationTest2()
        {
            var caretIndex = 1;
            var text = "0";
            var altText = "0";
            var maxLength = -1;

            for (int i = 1; i < 6; i++)
            {
                text += i;
                caretIndex++;

                TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, maxChars: 5);

                Assert.AreEqual($"{i}"[0], text[i]);
                Assert.AreEqual(i + 1, caretIndex);
                Assert.AreEqual(text, altText);
            }
            Assert.AreEqual(5, maxLength);
        }

        [TestMethod]
        public void TextBoxValidationTest3()
        {
            var caretIndex = 1;
            var text = "190";
            var altText = "19";
            var maxLength = -1;

            TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, maxNumber: 255);
            Assert.AreEqual("190", text);

            caretIndex = 3;
            TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, specialChars: '.', maxNumber: 255);
            Assert.AreEqual("190.", text);
            Assert.AreEqual(4, caretIndex);

            text = "190.300";
            TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, specialChars: '.', maxNumber: 255);
            Assert.AreEqual("190.30", text);

            text = "005";
            altText = "00";
            TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, specialChars: '.', maxNumber: 255);
            Assert.AreEqual("005.", text);

            text = "0" + text;
            TextHelper.TextBoxValidation(ref caretIndex, ref text, ref altText, ref maxLength, specialChars: '.', maxNumber: 255);
            Assert.AreEqual("005.", text);
        }

        [TestMethod]
        public void DuplicateFileTest()
        {
            string[] names = ["New File", "New File 1", "New File.txt"];
            string[] names1 = ["New File", "New File 2"];

            // New file
            Assert.AreEqual("New File", FileHelper.DuplicateFile(Array.Empty<string>(), "New File"));
            Assert.AreEqual("New File 1", FileHelper.DuplicateFile(["New File"], "New File"));
            Assert.AreEqual("New File 2", FileHelper.DuplicateFile(names, "New File"));
            Assert.AreEqual("New File 1", FileHelper.DuplicateFile(names1, "New File"));
            Assert.AreEqual("New File.pdf", FileHelper.DuplicateFile(names, "New File.pdf"));
            Assert.AreEqual("New File 1.txt", FileHelper.DuplicateFile(names, "New File.txt"));

            string[] names2 = ["New File", "New File - Copy 1", "New File.txt"];
            string[] names3 = ["New File", "New File - Copy 2"];

            // Copy
            Assert.AreEqual("New File", FileHelper.DuplicateFile(Array.Empty<string>(), "New File", System.Windows.DragDropEffects.Copy));
            Assert.AreEqual("New File - Copy 1", FileHelper.DuplicateFile(["New File"], "New File", System.Windows.DragDropEffects.Copy));
            Assert.AreEqual("New File - Copy 2", FileHelper.DuplicateFile(names2, "New File", System.Windows.DragDropEffects.Copy));
            Assert.AreEqual("New File - Copy 1", FileHelper.DuplicateFile(names3, "New File", System.Windows.DragDropEffects.Copy));
            Assert.AreEqual("New File.pdf", FileHelper.DuplicateFile(names2, "New File.pdf", System.Windows.DragDropEffects.Copy));
            Assert.AreEqual("New File - Copy 1.txt", FileHelper.DuplicateFile(names2, "New File.txt", System.Windows.DragDropEffects.Copy));
        }

        [TestMethod]
        public void VerifyIconTest()
        {
            var goodIcon = "\uE8EA";
            var badIcon1 = "\u0050";
            var badIcon2 = "FOO";

            // Verify this does not throw an exception by simply executing it
            StyleHelper.VerifyIcon(goodIcon);
            
            Assert.Throws<ArgumentException>(() => StyleHelper.VerifyIcon(badIcon1));
            Assert.Throws<ArgumentException>(() => StyleHelper.VerifyIcon(badIcon2));
        }

        [TestMethod]
        public void HashTest()
        {
            var Adb_33_0_3_path = @"E:\Android_SDK\platform-tools_r33.0.3\adb.exe";
            var Adb_33_0_3_hash = "3B0C0331799D69225E1BA24E6CB0DFAB";

            var result = Security.CalculateWindowsFileHash(Adb_33_0_3_path);
            Assert.AreEqual(Adb_33_0_3_hash, result);
        }

        [TestMethod]
        public void ExtractRelativePathTest()
        {
            var fullPath = @"/sdcard/DCIM/New Folder 1/New File.txt";
            var parent = @"/sdcard/DCIM/";

            var result = FileHelper.ExtractRelativePath(fullPath, parent);
            Assert.AreEqual("New Folder 1/New File.txt", result);

            result = FileHelper.ExtractRelativePath(fullPath, parent[..^1]);
            Assert.AreEqual("New Folder 1/New File.txt", result);

            result = FileHelper.ExtractRelativePath("New File.txt", "New File.txt");
            Assert.AreEqual("New File.txt", result);
        }

        [TestMethod]
        public void PullUpdatesTest()
        {
            const string updatesRaw = @"/sdcard/Download/DCIM/.duolingo-5-2-4.apk|0|0|
/sdcard/Download/DCIM/.duolingo-5-2-4.apk|3|25|
/sdcard/Download/DCIM/.duolingo-5-2-4.apk|6|42|
/sdcard/Download/DCIM/.duolingo-5-2-4.apk|10|69|
/sdcard/Download/DCIM/.duolingo-5-2-4.apk|12|81|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|16|8|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|17|27|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|18|44|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|20|63|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|21|81|
/sdcard/Download/DCIM/com.android.chrome_89.0.4389.105-438910534_11lang_11feat_31174470a9b1266fa7d8c87e62ac9c88_apkmirror.com.apkm|23|99|
/sdcard/Download/DCIM/root-checker-6-5-0.apk|24|18|
/sdcard/Download/DCIM/root-checker-6-5-0.apk|25|38|
/sdcard/Download/DCIM/root-checker-6-5-0.apk|27|56|
/sdcard/Download/DCIM/root-checker-6-5-0.apk|28|77|
/sdcard/Download/DCIM/mobisaver/397860000-398046fa4.jpg|31|2|
/sdcard/Download/DCIM/mobisaver/397860000-398046fa4.jpg|32|29|
/sdcard/Download/DCIM/mobisaver/397860000-398046fa4.jpg|33|54|
/sdcard/Download/DCIM/mobisaver/397860000-398046fa4.jpg|35|81|
/sdcard/Download/DCIM/New Folder/DSC_0001 - Copy 1.JPG|36|10|
/sdcard/Download/DCIM/New Folder/DSC_0001 - Copy 1.JPG|38|42|
/sdcard/Download/DCIM/New Folder/DSC_0001 - Copy 1.JPG|39|72|
/sdcard/Download/DCIM/New Folder/DSC_0001.JPG|41|4|
/sdcard/Download/DCIM/New Folder/DSC_0001.JPG|42|34|
/sdcard/Download/DCIM/New Folder/DSC_0001.JPG|43|64|
/sdcard/Download/DCIM/New Folder/DSC_0001.JPG|45|96|
/sdcard/Download/DCIM/New Folder/DSC_0001_1 - Copy 1.JPG|46|46|
/sdcard/Download/DCIM/New Folder/DSC_0001_1.JPG|48|1|
/sdcard/Download/DCIM/New Folder/DSC_0001_1.JPG|49|53|
/sdcard/Download/DCIM/New Folder/DSC_0002 - Copy 1.JPG|50|4|
/sdcard/Download/DCIM/New Folder/DSC_0002 - Copy 1.JPG|52|35|
/sdcard/Download/DCIM/New Folder/DSC_0002 - Copy 1.JPG|53|63|
/sdcard/Download/DCIM/New Folder/DSC_0002 - Copy 1.JPG|54|92|
/sdcard/Download/DCIM/New Folder/DSC_0002.JPG|56|24|
/sdcard/Download/DCIM/New Folder/DSC_0002.JPG|57|53|
/sdcard/Download/DCIM/New Folder/DSC_0002.JPG|59|82|
/sdcard/Download/DCIM/New Folder/DSC_0002_1 - Copy 1.JPG|60|10|
/sdcard/Download/DCIM/New Folder/DSC_0002_1 - Copy 1.JPG|62|33|
/sdcard/Download/DCIM/New Folder/DSC_0002_1 - Copy 1.JPG|63|55|
/sdcard/Download/DCIM/New Folder/DSC_0002_1 - Copy 1.JPG|64|80|
/sdcard/Download/DCIM/New Folder/DSC_0002_1.JPG|66|3|
/sdcard/Download/DCIM/New Folder/DSC_0002_1.JPG|67|27|
/sdcard/Download/DCIM/New Folder/DSC_0002_1.JPG|68|50|
/sdcard/Download/DCIM/New Folder/DSC_0002_1.JPG|70|72|
/sdcard/Download/DCIM/New Folder/DSC_0002_1.JPG|71|97|
/sdcard/Download/DCIM/New Folder/DSC_0003 - Copy 1.JPG|73|90|
/sdcard/Download/DCIM/New Folder/DSC_0003.JPG|74|96|
/sdcard/Download/DCIM/New Folder/DSC_0003_1 - Copy 1.JPG|75|43|
/sdcard/Download/DCIM/New Folder/DSC_0003_1 - Copy 1.JPG|77|92|
/sdcard/Download/DCIM/New Folder/DSC_0003_1.JPG|78|38|
/sdcard/Download/DCIM/New Folder/DSC_0003_1.JPG|80|83|
/sdcard/Download/DCIM/New Folder/DSC_0004 - Copy 1.JPG|81|50|
/sdcard/Download/DCIM/New Folder/DSC_0004.JPG|83|24|
/sdcard/Download/DCIM/New Folder/DSC_0004.JPG|84|94|
/sdcard/Download/DCIM/New Folder/DSC_0004_1 - Copy 1.JPG|85|47|
/sdcard/Download/DCIM/New Folder/DSC_0004_1 - Copy 1.JPG|87|95|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170303-173019.png|88|94|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170312-223158.png|89|35|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170321-183427.png|91|100|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170321-193051.png|92|67|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170321-211752.png|94|50|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170324-205213.png|95|77|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170401-135422.png|96|77|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170406-082707.png|98|11|
/sdcard/Download/DCIM/New Folder 1/Screenshot_20170409-163142.png|99|54|";
            var updatesStringRows = updatesRaw.Split("\r\n").Select(r => r.Split('|', StringSplitOptions.RemoveEmptyEntries));
            var updates = updatesStringRows.Select(u => new AdbSyncProgressInfo(u[0], int.Parse(u[1]), int.Parse(u[2]), null));
            SyncFile file = new("/sdcard/Download/DCIM", AbstractFile.FileType.Folder);

            file.AddUpdates(updates);

            Assert.IsTrue(file.Children.Count > 0);
            Assert.IsTrue(file.Children.All(c => c.RelationFrom(file) is AbstractFile.RelationType.Ancestor));
            Assert.AreEqual(0, file.Children.Count(c => c.ProgressUpdates.Any(u => u is SyncErrorInfo)));
        }

        [TestMethod]
        public void GetFullNameTest()
        {
            Assert.AreEqual("adb.exe", FileHelper.GetFullName(@"E:\Android_SDK\platform-tools_r33.0.3\adb.exe"));

            Assert.AreEqual("root-checker-6-5-0.apk", FileHelper.GetFullName(@"/sdcard/ASUS/root-checker-6-5-0.apk"));

            Assert.AreEqual("root-checker-6-5-0.apk", FileHelper.GetFullName(@"root-checker-6-5-0.apk"));

            Assert.AreEqual("ASUS", FileHelper.GetFullName(@"/sdcard/ASUS/"));

            Assert.AreEqual("sdcard", FileHelper.GetFullName(@"/sdcard/"));

            Assert.AreEqual("/", FileHelper.GetFullName("/"));

            Assert.AreEqual(@"C:\", FileHelper.GetFullName(@"C:\"));
        }

        [TestMethod]
        public void ParentNameTest()
        {
            Assert.AreEqual("/sdcard", FileHelper.GetParentPath("/sdcard/a"));
            Assert.AreEqual("/", FileHelper.GetParentPath("/sdcard"));
            Assert.AreEqual("E:", FileHelper.GetParentPath("E:\\New folder"));
            Assert.AreEqual("E:", FileHelper.GetParentPath("E:\\"));
        }

        [TestMethod]
        public void BytePatternTest()
        {
            Assert.AreEqual(-1, ByteHelper.PatternAt(Encoding.Unicode.GetBytes("foobar"), [0, 0]));
            Assert.AreEqual(11, ByteHelper.PatternAt(Encoding.Unicode.GetBytes("foobar\0"), [0, 0]));
            Assert.AreEqual(12, ByteHelper.PatternAt(Encoding.Unicode.GetBytes("foobar\0"), [0, 0], evenAlign: true));
        }

        [TestMethod]
        public void FileSizeTest()
        {
            long number = (long)Math.Pow(Math.Pow(9, 9), 2);
            NativeMethods.FILESIZE size = new(number);

            Assert.AreEqual(number, size.GetSize());
        }

        public class TestItem(int value) : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public int Value { get; set; } = value;
        }

        [TestMethod]
        public void AddRange_TimingCheck()
        {
            // Arrange
            var list = new ObservableList<TestItem>();
            var singleItem = new List<TestItem> { new(1) };
            var multipleItems = Enumerable.Range(0, 10).Select(i => new TestItem(i)).ToList();

            var multipleItemsTime = TimeSpan.Zero;
            for (int i = 0; i < 1000; i++)
            {
                list = [];

                var stopwatch = Stopwatch.StartNew();
                list.AddRange(multipleItems);
                stopwatch.Stop();

                multipleItemsTime += stopwatch.Elapsed;
            }
            
            // Ensure items were added correctly
            Assert.AreEqual(10, list.Count);

            var singleItemTime = TimeSpan.Zero;
            for (int i = 0; i < 1000; i++)
            {
                list = [];

                var stopwatch = Stopwatch.StartNew();
                list.AddRange(singleItem);
                stopwatch.Stop();

                singleItemTime += stopwatch.Elapsed;
            }

            // Output the timing results
            var oneItemTime = singleItemTime.TotalMilliseconds;
            var totalMulti = multipleItemsTime.TotalMilliseconds;

            Console.WriteLine($"1 vs each of 10 overhead:       {oneItemTime - totalMulti / 10:F3} ns");
            Console.WriteLine($"Average for 1 item:             {oneItemTime:F3} ns");
            Console.WriteLine($"Average for each of 10 items:   {totalMulti / 10:F3} ns");
            Console.WriteLine($"Average for 10 items:           {totalMulti:F3} ns");

            // Ensure items were added correctly
            Assert.AreEqual(1, list.Count);
        }

        [TestMethod]
        public void FindTest()
        {
            ObservableList<TestItem> list = [new(8), new(3), new(22)];
            ObservableList<TestItem> emptyList = [];

            Assert.AreEqual(3, list.Find(i => i.Value == 3).Value);
            Assert.AreEqual(null, list.Find(i => i.Value == 50));
            Assert.AreEqual(null, emptyList.Find(i => i.Value == 50));
        }

        [TestMethod]
        public void FileClassFromDescriptor()
        {
            var descriptor = new FileDescriptor
            {
                ChangeTimeUtc = DateTime.Now,
                Length = 1024,
                Name = "example.txt",
            };
            var file = new FileClass(descriptor);

            Assert.AreEqual("example.txt", file.FullName);
            Assert.AreEqual(1024, file.Size.Value);
            Assert.AreEqual(descriptor.ChangeTimeUtc, file.ModifiedTime);
            Assert.AreEqual(AbstractFile.FileType.File, file.Type);

            var emptyDirDescriptor = new FileDescriptor
            {
                ChangeTimeUtc = DateTime.Now,
                Name = "Directory",
                IsDirectory = true,
            };
            var emptyDir = new FileClass(emptyDirDescriptor)
                { PathType = AbstractFile.FilePathType.Windows };

            Assert.AreEqual("Directory", emptyDir.FullName);
            Assert.AreEqual(emptyDirDescriptor.ChangeTimeUtc, emptyDir.ModifiedTime);
            Assert.IsTrue(emptyDir.IsDirectory);
            Assert.AreEqual(AbstractFile.FileType.Folder, emptyDir.Type);
            Assert.AreEqual(AbstractFile.FilePathType.Windows, emptyDir.PathType);
        }
    }

    [TestClass]
    public class DevicePredicateTests
    {
        // Helpers

        private static LogicalDeviceViewModel MakeLogical(string id, string name, DeviceType type, DeviceStatus status, string ip = "")
            => new(LogicalDevice.From(new DeviceSnapshot(id, name, status, type, RootStatus.Unchecked, ip, default)));

        private static ServiceDeviceViewModel MakeService(string id, string ip, ServiceDevice.PairingMode mode, ServiceConnectionKind kind = ServiceConnectionKind.Pairing)
            => new(new ServiceDevice(id, ip, "5555", kind) { MdnsType = mode });

        private static EmulatorPackageDeviceViewModel MakeEmulatorPackage(string avdName, DeviceStatus status = DeviceStatus.Ok)
        {
            var device = new EmulatorPackageDevice(avdName) { Status = status };
            return new EmulatorPackageDeviceViewModel(device);
        }

        private static LogicalDeviceViewModel MakeEmulatorLogical(string id, string avdName, DeviceStatus status = DeviceStatus.Ok)
        {
            var vm = MakeLogical(id, avdName, DeviceType.Emulator, status);
            vm.SetAvdName(avdName);
            return vm;
        }

        private static bool Eval(DeviceViewModel device) => DeviceHelper.EvaluateDevicePredicate(device, Data.DevicesObject);

        [TestInitialize]
        public void Setup()
        {
            Data.Settings = new AppSettings();
            Data.DevicesObject = new Devices();
            // Remove the default VMs added by Devices() so tests start with a clean UIList
            Data.DevicesObject.UIList.Clear();
        }

        // ── MdnsDeviceViewModel ────────────────────────────────────────────────

        [TestMethod]
        public void MdnsDevice_HiddenWhenMdnsDisabled()
        {
            Data.Settings.EnableMdns = false;
            var vm = new MdnsDeviceViewModel(new MdnsDevice());
            Assert.IsFalse(Eval(vm));
        }

        [TestMethod]
        public void MdnsDevice_ShownWhenMdnsEnabled()
        {
            Data.Settings.EnableMdns = true;
            var vm = new MdnsDeviceViewModel(new MdnsDevice());
            Assert.IsTrue(Eval(vm));
        }

        // ── LogicalDeviceViewModel — open device always shown ─────────────────

        [TestMethod]
        public void OpenLogicalDevice_AlwaysShown()
        {
            var vm = MakeLogical("USB1", "Phone", DeviceType.Local, DeviceStatus.Ok);
            Data.DevicesObject.UIList.Add(vm);
            // Simulate open by setting DeviceToOpen (IsOpen is driven by DeviceToOpen match)
            Data.DevicesObject.DeviceToOpen = vm;
            Assert.IsTrue(Eval(vm));
        }

        // ── DeviceType.Service logical — offline ─────────────────────────────

        [TestMethod]
        public void ServiceLogical_Offline_NoMatchingService_Shown()
        {
            var vm = MakeLogical("svc.1", "Svc", DeviceType.Service, DeviceStatus.Offline, "192.168.1.10");
            Data.DevicesObject.UIList.Add(vm);
            // No ServiceDeviceViewModel with that IP
            Assert.IsTrue(Eval(vm));
        }

        [TestMethod]
        public void ServiceLogical_Offline_MatchingServiceExists_Hidden()
        {
            var svc = MakeService("svc_adb-tls-pairing._tcp.", "192.168.1.10", ServiceDevice.PairingMode.PairingCode);
            var logical = MakeLogical("svc.1", "Svc", DeviceType.Service, DeviceStatus.Offline, "192.168.1.10");
            Data.DevicesObject.UIList.Add(svc);
            Data.DevicesObject.UIList.Add(logical);
            Assert.IsFalse(Eval(logical));
        }

        // ── DeviceType.Service logical — online ──────────────────────────────

        [TestMethod]
        public void ServiceLogical_Online_NoDuplicateRemote_Shown()
        {
            var vm = MakeLogical("svc.2", "Svc", DeviceType.Service, DeviceStatus.Ok, "192.168.1.20");
            Data.DevicesObject.UIList.Add(vm);
            Assert.IsTrue(Eval(vm));
        }

        [TestMethod]
        public void ServiceLogical_Online_DuplicateRemoteOnline_Shown()
        {
            var remote = MakeLogical("192.168.1.20:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.20");
            var svcLogical = MakeLogical("adb-SERIALABC-QXjCrW._adb-tls-connect._tcp.", "Phone", DeviceType.Service, DeviceStatus.Ok, "192.168.1.20");
            Data.DevicesObject.UIList.Add(remote);
            Data.DevicesObject.UIList.Add(svcLogical);
            Assert.IsTrue(Eval(svcLogical));
            Assert.IsFalse(Eval(remote));
        }

        // ── DeviceType.Remote ─────────────────────────────────────────────────

        [TestMethod]
        public void RemoteDevice_NoUsbDuplicate_Shown()
        {
            var vm = MakeLogical("192.168.1.30:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.30");
            Data.DevicesObject.UIList.Add(vm);
            Assert.IsTrue(Eval(vm));
        }

        [TestMethod]
        public void RemoteDevice_UsbDuplicateByIp_Hidden()
        {
            var usb = MakeLogical("SERIALABC", "Phone", DeviceType.Local, DeviceStatus.Ok, "192.168.1.30");
            var remote = MakeLogical("192.168.1.30:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.30");
            Data.DevicesObject.UIList.Add(usb);
            Data.DevicesObject.UIList.Add(remote);
            Assert.IsFalse(Eval(remote));
        }

        [TestMethod]
        public void RemoteDevice_UsbDuplicateById_Hidden()
        {
            var usb = MakeLogical("SERIALABC", "Phone", DeviceType.Local, DeviceStatus.Ok, "");
            var remote = MakeLogical("SERIALABC:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.31");
            Data.DevicesObject.UIList.Add(usb);
            Data.DevicesObject.UIList.Add(remote);
            Assert.IsFalse(Eval(remote));
        }

        // ── HistoryDeviceViewModel ────────────────────────────────────────────

        [TestMethod]
        public void HistoryDevice_ShownWhenNoMatchingLogicalOrService()
        {
            Data.Settings.SaveDevices = true;
            var hist = new HistoryDeviceViewModel(new HistoryDevice("192.168.1.40", "5555", "OldPhone"));
            Data.DevicesObject.UIList.Add(hist);
            Assert.IsTrue(Eval(hist));
        }

        [TestMethod]
        public void HistoryDevice_HiddenWhenSaveDevicesFalse()
        {
            Data.Settings.SaveDevices = false;
            var hist = new HistoryDeviceViewModel(new HistoryDevice("192.168.1.40", "5555", "OldPhone"));
            Data.DevicesObject.UIList.Add(hist);
            Assert.IsFalse(Eval(hist));
        }

        [TestMethod]
        public void HistoryDevice_HiddenWhenMatchingLogicalExists()
        {
            Data.Settings.SaveDevices = true;
            var logical = MakeLogical("192.168.1.50:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.50");
            var hist = new HistoryDeviceViewModel(new HistoryDevice("192.168.1.50", "5555", "OldPhone"));
            Data.DevicesObject.UIList.Add(logical);
            Data.DevicesObject.UIList.Add(hist);
            Assert.IsFalse(Eval(hist));
        }

        [TestMethod]
        public void HistoryDevice_HiddenWhenMatchingServiceExists()
        {
            Data.Settings.SaveDevices = true;
            var svc = MakeService("svc_adb-tls-pairing._tcp.", "192.168.1.60", ServiceDevice.PairingMode.PairingCode);
            var hist = new HistoryDeviceViewModel(new HistoryDevice("192.168.1.60", "5555", "OldPhone"));
            Data.DevicesObject.UIList.Add(svc);
            Data.DevicesObject.UIList.Add(hist);
            Assert.IsFalse(Eval(hist));
        }

        // ── ServiceDeviceViewModel ────────────────────────────────────────────

        [TestMethod]
        public void ServiceDevice_ConnectKind_AlwaysHidden()
        {
            var vm = MakeService("svc_connect._tcp.", "192.168.1.70", ServiceDevice.PairingMode.PairingCode, ServiceConnectionKind.Connect);
            Data.DevicesObject.UIList.Add(vm);
            Assert.IsFalse(Eval(vm));
        }

        [TestMethod]
        public void ServiceDevice_PairingCode_ShownWhenNoConflicts()
        {
            var vm = MakeService("svc_code_adb-tls-pairing._tcp.", "192.168.1.80", ServiceDevice.PairingMode.PairingCode);
            Data.DevicesObject.UIList.Add(vm);
            Assert.IsTrue(Eval(vm));
        }

        [TestMethod]
        public void ServiceDevice_PairingCode_HiddenWhenQrServiceSameIp()
        {
            var qr = MakeService("svc_qr_adb-tls-pairing._tcp.", "192.168.1.90", ServiceDevice.PairingMode.QrCode);
            var code = MakeService("svc_code2_adb-tls-pairing._tcp.", "192.168.1.90", ServiceDevice.PairingMode.PairingCode);
            Data.DevicesObject.UIList.Add(qr);
            Data.DevicesObject.UIList.Add(code);
            Assert.IsFalse(Eval(code));
        }

        [TestMethod]
        public void ServiceDevice_PairingCode_HiddenWhenOnlineLogicalSameIp()
        {
            var logical = MakeLogical("192.168.1.100:5555", "Phone", DeviceType.Remote, DeviceStatus.Ok, "192.168.1.100");
            var svc = MakeService("svc_code3_adb-tls-pairing._tcp.", "192.168.1.100", ServiceDevice.PairingMode.PairingCode);
            Data.DevicesObject.UIList.Add(logical);
            Data.DevicesObject.UIList.Add(svc);
            Assert.IsFalse(Eval(svc));
        }

        // ── WsaPkgDeviceViewModel ─────────────────────────────────────────────

        [TestMethod]
        public void WsaPkg_Offline_Hidden()
        {
            var wsa = new WsaPkgDeviceViewModel(new WsaPkgDevice() /* Status=Offline by default */);
            Data.DevicesObject.UIList.Add(wsa);
            Assert.IsFalse(Eval(wsa));
        }

        [TestMethod]
        public void WsaPkg_Online_NoWsaLogical_Shown()
        {
            var wsaDevice = new WsaPkgDevice();
            wsaDevice.Status = DeviceStatus.Ok;
            var wsa = new WsaPkgDeviceViewModel(wsaDevice);
            Data.DevicesObject.UIList.Add(wsa);
            Assert.IsTrue(Eval(wsa));
        }

        [TestMethod]
        public void WsaPkg_Online_WsaLogicalExists_Hidden()
        {
            var wsaDevice = new WsaPkgDevice();
            wsaDevice.Status = DeviceStatus.Ok;
            var wsa = new WsaPkgDeviceViewModel(wsaDevice);
            var wsaLogical = MakeLogical("localhost:5555", "Windows Subsystem", DeviceType.WSA, DeviceStatus.Ok, "");
            Data.DevicesObject.UIList.Add(wsa);
            Data.DevicesObject.UIList.Add(wsaLogical);
            Assert.IsFalse(Eval(wsa));
        }

        // ── WSA logical offline ───────────────────────────────────────────────

        [TestMethod]
        public void WsaLogical_Offline_Hidden()
        {
            var vm = MakeLogical("localhost:5555", "WSA", DeviceType.WSA, DeviceStatus.Offline, "");
            Data.DevicesObject.UIList.Add(vm);
            Assert.IsFalse(Eval(vm));
        }

        // ── EmulatorPackageDeviceViewModel ────────────────────────────────────

        [TestMethod]
        public void EmulatorPkg_Offline_Hidden()
        {
            Data.Settings.EnableEmulatorDiscovery = true;
            var pkg = MakeEmulatorPackage("Pixel_7", DeviceStatus.Offline);
            Data.DevicesObject.UIList.Add(pkg);
            Assert.IsFalse(Eval(pkg));
        }

        [TestMethod]
        public void EmulatorPkg_Ok_NoLogical_Shown()
        {
            Data.Settings.EnableEmulatorDiscovery = true;
            var pkg = MakeEmulatorPackage("Pixel_7");
            Data.DevicesObject.UIList.Add(pkg);
            Assert.IsTrue(Eval(pkg));
        }

        [TestMethod]
        public void EmulatorPkg_Ok_LogicalSameAvd_Hidden()
        {
            Data.Settings.EnableEmulatorDiscovery = true;
            var pkg = MakeEmulatorPackage("Pixel_7");
            var logical = MakeEmulatorLogical("emulator-5554", "Pixel_7");
            Data.DevicesObject.UIList.Add(pkg);
            Data.DevicesObject.UIList.Add(logical);
            Assert.IsFalse(Eval(pkg));
        }

        [TestMethod]
        public void EmulatorPkg_Ok_LogicalDifferentAvd_Shown()
        {
            Data.Settings.EnableEmulatorDiscovery = true;
            var pkg = MakeEmulatorPackage("Pixel_4");
            var logical = MakeEmulatorLogical("emulator-5554", "Pixel_7");
            Data.DevicesObject.UIList.Add(pkg);
            Data.DevicesObject.UIList.Add(logical);
            Assert.IsTrue(Eval(pkg));
        }

        [TestMethod]
        public void EmulatorPkg_SettingOff_Hidden()
        {
            Data.Settings.EnableEmulatorDiscovery = false;
            var pkg = MakeEmulatorPackage("Pixel_7");
            Data.DevicesObject.UIList.Add(pkg);
            Assert.IsFalse(Eval(pkg));
        }

        // ── Duplicate name → UseIdForName ─────────────────────────────────────

        [TestMethod]
        public void DuplicateName_UseIdForName_SetToTrue()
        {
            // UseIdForName is set when there are more than 1 other devices with the same name and different IPs.
            var vm1 = MakeLogical("SERIAL001", "MyPhone", DeviceType.Local, DeviceStatus.Ok, "192.168.1.1");
            var vm2 = MakeLogical("SERIAL002", "MyPhone", DeviceType.Local, DeviceStatus.Ok, "192.168.1.2");
            var vm3 = MakeLogical("SERIAL003", "MyPhone", DeviceType.Local, DeviceStatus.Ok, "192.168.1.3");
            Data.DevicesObject.UIList.Add(vm1);
            Data.DevicesObject.UIList.Add(vm2);
            Data.DevicesObject.UIList.Add(vm3);

            Eval(vm1);

            Assert.IsTrue(vm1.UseIdForName);
        }

        [TestMethod]
        public void UniqueName_UseIdForName_RemainsDefault()
        {
            var vm = MakeLogical("SERIAL001", "MyPhone", DeviceType.Local, DeviceStatus.Ok, "192.168.1.1");
            Data.DevicesObject.UIList.Add(vm);

            Eval(vm);

            Assert.IsFalse(vm.UseIdForName);
        }

        [TestMethod]
        public void ParseBatteryPropertyValueTest()
        {
            const string chargingUsb = """
                Result: Parcel(
                  0x00000000: 00000000 00000000 00000001 fffacfe0 '................'
                  0x00000010: ffffffff                            '....            ')
                """;

            const string discharging = """
                Result: Parcel(
                  0x00000000: 00000000 00000000 00000001 fffde439 '............9...'
                  0x00000010: ffffffff                            '....            ')
                """;

            Assert.AreEqual(-340000L, ADBService.ParseBatteryPropertyValue(chargingUsb));
            Assert.AreEqual(-138183L, ADBService.ParseBatteryPropertyValue(discharging));
            Assert.IsNull(ADBService.ParseBatteryPropertyValue("Result: Parcel(00000000    '....')"));
            Assert.IsNull(ADBService.ParseBatteryPropertyValue("Result: Parcel(Error: 0xffffffffffffffb6 \"Not a data message\")"));
        }

        [TestMethod]
        public void ParseShellIdentityTest()
        {
            var stdout = """
                shell
                uid=2000(shell) gid=2000(shell) groups=2000(shell),1003(graphics),1015(sdcard_rw),1023(media_rw)
                """;

            var identity = ShellAccessHelper.ParseShellIdentity(stdout);
            Assert.IsNotNull(identity);
            Assert.AreEqual("shell", identity.UserName);
            Assert.AreEqual(2000, identity.Uid);
            Assert.AreEqual(2000, identity.Gid);
            Assert.IsFalse(identity.IsRoot);
            CollectionAssert.IsSubsetOf(new[] { 1003, 1015, 1023 }, identity.Groups.ToArray());

            var rootStdout = """
                root
                uid=0(root) gid=0(root) groups=0(root)
                """;
            var root = ShellAccessHelper.ParseShellIdentity(rootStdout);
            Assert.IsNotNull(root);
            Assert.IsTrue(root.IsRoot);
        }

        [TestMethod]
        public void ParseLocationInfoTest()
        {
            var stdout = """
                media_rw§media_rw§1023§1023§771§2024-01-02 03:04:05.000000000 +0000§2024-01-02 03:04:05.000000000 +0000§2024-01-02 03:04:05.000000000 +0000
                ADB_ACCESS:101
                """.Replace('§', AdbExplorerConst.ADB_FIELD_SEP);

            var info = ShellAccessHelper.ParseLocationInfo(stdout);
            Assert.IsNotNull(info);
            Assert.AreEqual("media_rw", info.Value.User);
            Assert.AreEqual(1023, info.Value.OwnerUid);
            var expectedMode = (System.IO.UnixFileMode)Convert.ToInt32("771", 8);
            Assert.AreEqual(expectedMode, info.Value.Permissions);
            Assert.AreEqual(AccessMask.Read | AccessMask.Execute, info.Value.ProbedAccess);
        }

        [TestMethod]
        public void ResolveEffectiveAccessTest()
        {
            var identity = new ShellIdentity("shell", 2000, 2000, new HashSet<int> { 2000, 1023 });
            var mode = (System.IO.UnixFileMode)Convert.ToInt32("771", 8);

            var effective = ShellAccessHelper.ResolveEffective(mode, 1023, 1023, identity);
            Assert.AreEqual(AccessMask.Read | AccessMask.Write | AccessMask.Execute, effective);

            var otherOnly = ShellAccessHelper.ResolveEffective(mode, 0, 0, identity);
            Assert.AreEqual(AccessMask.Execute, otherOnly);
        }

        [TestMethod]
        public void ResolveLocationAccess_AssumesFullAccessWhenNothingKnown()
        {
            var identity = new ShellIdentity("shell", 2000, 2000, new HashSet<int> { 2000 });

            var noInfo = ShellAccessHelper.ResolveLocationAccess("/sdcard/Download/folder.zip/New Folder", null, identity, DriveRestrictions.None);
            Assert.AreEqual(AccessMask.All, noInfo);

            var emptyInfo = new LocationInfo(null, null, null, null, null, AccessMask.None, null, null, null);
            var withEmpty = ShellAccessHelper.ResolveLocationAccess("/sdcard/Download/folder.zip/New Folder", emptyInfo, identity, DriveRestrictions.None);
            Assert.AreEqual(AccessMask.All, withEmpty);
        }

        [TestMethod]
        public void ResolveLocationAccess_UsesPermissionsWhenProbeLacksWrite()
        {
            var identity = new ShellIdentity("shell", 2000, 2000, new HashSet<int> { 1023 });
            var mode = (System.IO.UnixFileMode)Convert.ToInt32("771", 8);
            var info = new LocationInfo("media_rw", "media_rw", 1023, 1023, mode, AccessMask.Read | AccessMask.Execute, null, null, null);

            var access = ShellAccessHelper.ResolveLocationAccess("/sdcard/folder.zip/subdir", info, identity, DriveRestrictions.None);

            Assert.IsTrue(access.HasFlag(AccessMask.Write));
            Assert.IsTrue(access.HasFlag(AccessMask.Read));
        }

        [TestMethod]
        public void FuseProtectedAndroidRootTest()
        {
            Assert.IsTrue(ShellAccessHelper.IsFuseProtectedAndroidRoot("/sdcard/Android"));
            Assert.IsTrue(ShellAccessHelper.IsFuseProtectedAndroidRoot("/sdcard/Android/"));
            Assert.IsTrue(ShellAccessHelper.IsFuseProtectedAndroidRoot("/storage/emulated/0/Android"));
            Assert.IsFalse(ShellAccessHelper.IsFuseProtectedAndroidRoot("/sdcard/Android/data"));

            var access = ShellAccessHelper.ResolveLocationAccess(
                "/sdcard/Android",
                new LocationInfo(null, null, null, null, null, AccessMask.Read | AccessMask.Write | AccessMask.Execute, null, null, null),
                new ShellIdentity("shell", 2000, 2000, new HashSet<int> { 2000 }),
                DriveRestrictions.None);

            Assert.IsTrue(access.HasFlag(AccessMask.Write));
            Assert.IsTrue(access.HasFlag(AccessMask.Read));
        }
    }
}
