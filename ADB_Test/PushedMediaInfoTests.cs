#nullable enable

using ADB_Explorer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static ADB_Explorer.Services.ThumbnailService;

namespace ADB_Test;

/// <summary>The image or video data read from Windows files as they're pushed, kept with the thumbnail cache.</summary>
[TestClass]
public class PushedMediaInfoTests
{
    [TestMethod]
    public void PushedMedia_PerceivedTypes()
    {
        Assert.AreEqual(MediaType.images, GetPerceivedMediaType(".jpg"));
        Assert.AreEqual(MediaType.images, GetPerceivedMediaType(".PNG"));
        Assert.AreEqual(MediaType.video, GetPerceivedMediaType(".mp4"));
        Assert.IsNull(GetPerceivedMediaType(".txt"));
        Assert.IsNull(GetPerceivedMediaType(""));
    }

    [TestMethod]
    public void PushedMedia_FolderFilesMapToTheirDevicePaths()
    {
        var local = Directory.CreateTempSubdirectory("ADB Explorer media ").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(local, "Trip", "Day 1"));
            File.WriteAllBytes(Path.Combine(local, "Trip", "Day 1", "a.jpg"), []);
            File.WriteAllBytes(Path.Combine(local, "Trip", "notes.txt"), []);
            File.WriteAllBytes(Path.Combine(local, "Trip", "b.mp4"), []);

            var files = PushedMediaFiles(Path.Combine(local, "Trip"), "/sdcard/DCIM/Trip", isDirectory: true)
                .Select(file => file.Remote)
                .Order(StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "/sdcard/DCIM/Trip/Day 1/a.jpg", "/sdcard/DCIM/Trip/b.mp4" }, files);

            // A single file is itself, when it's media
            var single = Path.Combine(local, "Trip", "b.mp4");
            Assert.AreEqual((single, "/sdcard/b.mp4"), PushedMediaFiles(single, "/sdcard/b.mp4", isDirectory: false).Single());
            Assert.IsFalse(PushedMediaFiles(Path.Combine(local, "Trip", "notes.txt"), "/sdcard/notes.txt", isDirectory: false).Any());

            // A folder in a zip or on a phone has no files here
            Assert.IsFalse(PushedMediaFiles(@"::{phone}\DCIM", "/sdcard/DCIM", isDirectory: true).Any());
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void PushedMedia_ReadsDimensionsAndExposure()
    {
        var local = Directory.CreateTempSubdirectory("ADB Explorer media ").FullName;
        try
        {
            var photo = Path.Combine(local, "photo.jpg");
            WriteJpeg(photo, 40, 30, fNumber: (18, 10), iso: 200, exposure: (1, 125));

            var info = ReadMediaInfo(photo);
            Assert.IsNotNull(info);
            Assert.IsTrue(info.Value.IsInfoOnly);
            Assert.AreEqual(MediaType.images, info.Value.Type);
            Assert.AreEqual(new Size(40, 30), info.Value.Resolution);
            Assert.AreEqual(1.8, info.Value.FNumber!.Value, 0.001);
            Assert.AreEqual(200, info.Value.ISO);
            Assert.AreEqual(1 / 125d, info.Value.ExposureTime!.Value, 0.0001);

            // What's not media isn't read
            var text = Path.Combine(local, "notes.txt");
            File.WriteAllText(text, "notes");
            Assert.IsNull(ReadMediaInfo(text));
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }

    [TestMethod]
    public void PushedMedia_EntriesKeepTheirDataThroughTheCsvAndThumbnails()
    {
        var pushed = new ThumbnailInfo("", MediaType.images, new Size(4000, 3000), null, DateTime.MinValue, FNumber: 1.8, ISO: 200, ExposureTime: 0.008);
        Assert.IsTrue(pushed.IsInfoOnly);
        Assert.AreEqual(pushed, ThumbnailInfo.FromCsv(pushed.ToCsv()));

        // A thumbnail from the device's media database takes the pushed data, which wins over what it lacks or has
        var device = new ThumbnailInfo("1234.jpg", MediaType.images, new Size(400, 300), null, DateTime.MinValue, ISO: 100);
        var merged = device.WithMediaInfo(pushed);
        Assert.IsFalse(merged.IsInfoOnly);
        Assert.AreEqual("1234.jpg", merged.Id);
        Assert.AreEqual(new Size(4000, 3000), merged.Resolution);
        Assert.AreEqual(200, merged.ISO);
        Assert.AreEqual(1.8, merged.FNumber);

        // Data the pushed file doesn't have is left as it was
        Assert.AreEqual(device, device.WithMediaInfo(new ThumbnailInfo("", MediaType.images, null, null, DateTime.MinValue)));
    }

    private static void WriteJpeg(string path, int width, int height, (uint, uint) fNumber, ushort iso, (uint, uint) exposure)
    {
        var pixels = new byte[width * height * 3];
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/exif/{ushort=33437}", Rational(fNumber));
        metadata.SetQuery("/app1/ifd/exif/{ushort=34855}", iso);
        metadata.SetQuery("/app1/ifd/exif/{ushort=33434}", Rational(exposure));

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        using var stream = File.Create(path);
        encoder.Save(stream);

        // WIC's rational: the numerator in the low half, the denominator in the high
        static ulong Rational((uint Numerator, uint Denominator) value) => ((ulong)value.Denominator << 32) | value.Numerator;
    }
}
