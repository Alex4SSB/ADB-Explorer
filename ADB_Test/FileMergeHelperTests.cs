using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADB_Test;

[TestClass]
public class FileMergeHelperTests
{
    [TestMethod]
    public void AreIdenticalForMerge_MatchingSizeAndMtime_ReturnsTrue()
    {
        var t = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(FileMergeHelper.AreIdenticalForMerge(100, t, 100, t));
    }

    [TestMethod]
    public void AreIdenticalForMerge_SizeMismatch_ReturnsFalse()
    {
        var t = DateTime.UtcNow;
        Assert.IsFalse(FileMergeHelper.AreIdenticalForMerge(100, t, 101, t));
    }

    [TestMethod]
    public void AreIdenticalForMerge_MtimeWithinTolerance_ReturnsTrue()
    {
        var a = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var b = a.AddSeconds(1.5);
        Assert.IsTrue(FileMergeHelper.AreIdenticalForMerge(50, a, 50, b));
    }

    [TestMethod]
    public void AreIdenticalForMerge_MtimeOutsideTolerance_ReturnsFalse()
    {
        var a = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var b = a.AddSeconds(2.5);
        Assert.IsFalse(FileMergeHelper.AreIdenticalForMerge(50, a, 50, b));
    }

    [TestMethod]
    public void AreIdenticalForMerge_MissingDate_ReturnsFalse()
    {
        var t = DateTime.UtcNow;
        Assert.IsFalse(FileMergeHelper.AreIdenticalForMerge(10, (DateTime?)null, 10, t));
        Assert.IsFalse(FileMergeHelper.AreIdenticalForMerge(10, t, 10, null));
    }

    [TestMethod]
    public void ExpandConflicts_MatchingFolders_ListsOnlyNestedFileConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), "AdbExplorerMergeRecursive", Guid.NewGuid().ToString("N"));
        var sourceFolder = Path.Combine(root, "src", "Photos");
        var destRoot = Path.Combine(root, "dest");
        var destFolder = Path.Combine(destRoot, "Photos");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceFolder, "sub"));
            Directory.CreateDirectory(Path.Combine(destFolder, "sub"));

            File.WriteAllBytes(Path.Combine(sourceFolder, "new.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(sourceFolder, "diff.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(sourceFolder, "sub", "nested.bin"), new byte[7]);

            File.WriteAllBytes(Path.Combine(destFolder, "diff.bin"), new byte[20]);
            File.WriteAllBytes(Path.Combine(destFolder, "sub", "nested.bin"), new byte[7]);
            // Match nested size+mtime roughly by copying
            File.SetLastWriteTimeUtc(
                Path.Combine(destFolder, "sub", "nested.bin"),
                File.GetLastWriteTimeUtc(Path.Combine(sourceFolder, "sub", "nested.bin")));

            var candidates = new FileMergeHelper.ConflictCandidate[]
            {
                new(sourceFolder, "Photos", true, null, null),
            };

            FileMergeHelper.DestEntry GetDest(string name) => name == "Photos"
                ? new(true, true, null, null)
                : new(false, false, null, null);

            var rows = FileMergeHelper.ExpandConflicts(
                candidates,
                destRoot,
                GetDest,
                targetIsWindows: true,
                deviceId: null,
                StringComparer.OrdinalIgnoreCase);

            // Folder itself is not a conflict; new.bin is not (dest missing);
            // diff.bin + nested.bin are conflicts.
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEquivalent(
                new[] { @"Photos\diff.bin", @"Photos\sub\nested.bin" },
                rows.Select(r => r.Name).ToArray());
            Assert.IsFalse(rows.First(r => r.Name.EndsWith("diff.bin")).IsIdentical);
            Assert.IsTrue(rows.First(r => r.Name.EndsWith("nested.bin")).IsIdentical);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void FilterSyncTreeByConflictResolution_SkipsUnresolvedConflicts_KeepsNewAndReplace()
    {
        var root = new SyncFile("/sdcard/Photos", AbstractFile.FileType.Folder);
        var keepNew = new SyncFile("/sdcard/Photos/new.bin") { Size = 1 };
        var replace = new SyncFile("/sdcard/Photos/diff.bin") { Size = 2 };
        var skip = new SyncFile("/sdcard/Photos/skip.bin") { Size = 3 };
        root.Children.Add(keepNew);
        root.Children.Add(replace);
        root.Children.Add(skip);

        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"Photos\diff.bin",
            @"Photos\skip.bin",
        };
        var replaceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"Photos\diff.bin",
        };

        Assert.IsTrue(FileMergeHelper.FilterSyncTreeByConflictResolution(
            root, "Photos", replaceSet, conflicts, '\\'));

        CollectionAssert.AreEquivalent(
            new[] { "new.bin", "diff.bin" },
            root.Children.Select(c => c.FullName).ToArray());
    }

    [TestMethod]
    public void FilterIdenticalPullTree_DropsMatchingLeaf_KeepsDiffering()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AdbExplorerMergeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var matchPath = Path.Combine(dir, "match.bin");
            var diffPath = Path.Combine(dir, "diff.bin");
            File.WriteAllBytes(matchPath, new byte[32]);
            File.WriteAllBytes(diffPath, new byte[16]);

            var matchInfo = new FileInfo(matchPath);
            var mtimeUtc = matchInfo.LastWriteTimeUtc;

            var root = new SyncFile("/sdcard/bench", AbstractFile.FileType.Folder);
            var match = new SyncFile("/sdcard/bench/match.bin")
            {
                Size = matchInfo.Length,
                UnixTime = (mtimeUtc - DateTime.UnixEpoch).TotalSeconds,
            };
            var diff = new SyncFile("/sdcard/bench/diff.bin")
            {
                Size = 99,
                UnixTime = (mtimeUtc - DateTime.UnixEpoch).TotalSeconds,
            };
            root.Children.Add(match);
            root.Children.Add(diff);

            Assert.IsTrue(FileMergeHelper.FilterIdenticalPullTree(root, dir));
            Assert.AreEqual(1, root.Children.Count);
            Assert.AreEqual("diff.bin", root.Children[0].FullName);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [TestMethod]
    public void FilterIdenticalPullTree_AllIdentical_ReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AdbExplorerMergeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "only.bin");
            File.WriteAllBytes(path, new byte[8]);
            var info = new FileInfo(path);

            var file = new SyncFile("/sdcard/only.bin")
            {
                Size = info.Length,
                UnixTime = (info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds,
            };

            Assert.IsFalse(FileMergeHelper.FilterIdenticalPullTree(file, path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
