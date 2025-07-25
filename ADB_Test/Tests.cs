using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ADB_Test
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public void ToSizeTest()
        {
            var testVals = new Dictionary<ulong, string>()
            {
                { 0, "0B" },
                { 300, "300B" },
                { 33000, "32.2KB" }, // 32.226
                { 500690, "489KB" }, // 488.955
                { 1024204, "1MB" }, // 1.0002
                { 1200100, "1.1MB" }, // 1.145
                { 3400200100, "3.2GB" }, // 1.667
                { 1200300400500, "1.1TB" } // 1.092
            };

            foreach (var item in testVals)
            {
                Assert.IsTrue(item.Key.BytesToSize() == item.Value);
            }

        }

        [TestMethod]
        public void ToTimeTest()
        {
            Assert.AreEqual("50ms", UnitConverter.ToTime(.05m));
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

            Assert.ThrowsException<ArgumentException>(() => StyleHelper.VerifyIcon(badIcon1));
            Assert.ThrowsException<ArgumentException>(() => StyleHelper.VerifyIcon(badIcon2));
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
            Assert.AreEqual((ulong)1024, file.Size.Value);
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

        [TestMethod]
        public void KnownFolderTest()
        {
            // The other checks are now irrelevant and heve been removed since the method has changes

            Assert.AreEqual("F:", ExplorerHelper.ParseTreePath("This PC\\Sandisk Cruzer (F:)"));

            // Quick access in Windows 10 can have any folder, hence it is impossible to determine the actual path
            Assert.IsNull(ExplorerHelper.ParseTreePath("Quick access\\Pictures"));

            // These are virtual locations
            Assert.IsNull(ExplorerHelper.ParseTreePath("Libraries"));
            Assert.IsNull(ExplorerHelper.ParseTreePath("Network"));
            Assert.IsNull(ExplorerHelper.ParseTreePath("This PC"));
        }
    }
}
