using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;

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

            Assert.AreEqual("", FileClass.ExistingIndexes(names[..0], true));
            Assert.AreEqual(" - Copy 1", FileClass.ExistingIndexes(new[] { "" }, true));
            Assert.AreEqual(" - Copy 2", FileClass.ExistingIndexes(names, true));
            Assert.AreEqual(" - Copy 1", FileClass.ExistingIndexes(names1, true));
        }

        [TestMethod]
        public void VerifyIconTest()
        {
            var goodIcon = "\uE8EA";
            var badIcon1 = "\u0050";
            var badIcon2 = "FOO";

            // Verify this does not throw an exception by simply executing it
            StyleHelper.VerifyIcon(ref goodIcon);

            Assert.ThrowsException<ArgumentException>(() => StyleHelper.VerifyIcon(ref badIcon1));
            Assert.ThrowsException<ArgumentException>(() => StyleHelper.VerifyIcon(ref badIcon2));
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
        }
    }
}
