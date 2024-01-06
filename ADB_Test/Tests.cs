using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

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
                Assert.IsTrue(item.Key.ToSize() == item.Value);
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
        public void ExistingIndexesTest()
        {
            string[] names = { "", "1" };
            string[] names1 = { "", "2" };

            Assert.AreEqual("", FileClass.ExistingIndexes(names[..0])); // empty array
            Assert.AreEqual(" 1", FileClass.ExistingIndexes(new [] { "" }));
            Assert.AreEqual(" 2", FileClass.ExistingIndexes(names));
            Assert.AreEqual(" 1", FileClass.ExistingIndexes(names1));

            Assert.AreEqual("", FileClass.ExistingIndexes(names[..0], FileClass.CutType.Copy));
            Assert.AreEqual(" - Copy 1", FileClass.ExistingIndexes(new[] { "" }, FileClass.CutType.Copy));
            Assert.AreEqual(" - Copy 2", FileClass.ExistingIndexes(names, FileClass.CutType.Copy));
            Assert.AreEqual(" - Copy 1", FileClass.ExistingIndexes(names1, FileClass.CutType.Copy));
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
            var updatesStringRows = updatesRaw.Split('\n').Select(r => r.Split('|'));
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
            Assert.AreEqual("adb.exe", FilePath.GetFullName(@"E:\Android_SDK\platform-tools_r33.0.3\adb.exe"));

            Assert.AreEqual("root-checker-6-5-0.apk", FilePath.GetFullName(@"/sdcard/ASUS/root-checker-6-5-0.apk"));

            Assert.AreEqual("root-checker-6-5-0.apk", FilePath.GetFullName(@"root-checker-6-5-0.apk"));

            Assert.AreEqual("ASUS", FilePath.GetFullName(@"/sdcard/ASUS/"));

            Assert.AreEqual("/", FilePath.GetFullName("/"));
        }
    }
}
