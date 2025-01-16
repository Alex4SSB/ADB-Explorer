using System.Runtime.InteropServices.ComTypes;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public static class AdbDataFormats
{
    public static AdbDataFormat DragImage { get; } = new("DragImageBits");
    public static AdbDataFormat FileContents { get; } = new("FileContents");
    public static AdbDataFormat FileDescriptor { get; } = new("FileGroupDescriptorW");
    public static AdbDataFormat PasteSucceeded { get; } = new("Paste Succeeded");
    public static AdbDataFormat PerformedDropEffect { get; } = new("Performed DropEffect");
    public static AdbDataFormat PreferredDropEffect { get; } = new("Preferred DropEffect");
    public static AdbDataFormat DragLoop { get; } = new("InShellDragLoop");
    public static AdbDataFormat FileName { get; } = new("FileName");
    public static AdbDataFormat FileNameW { get; } = new("FileNameW");
    public static AdbDataFormat ShellidList { get; } = new("Shell IDList Array");
    public static AdbDataFormat FileDrop { get; } = new(DataFormats.FileDrop);
    public static AdbDataFormat AdbDrop { get; } = new(AdbExplorerConst.ADB_DRAG_FORMAT);

    private static Dictionary<short, string> DataFormatKeys => new()
    {
        { DragImage, nameof(DragImage) },
        { FileContents, nameof(FileContents) },
        { FileDescriptor, nameof(FileDescriptor) },
        { PasteSucceeded, nameof(PasteSucceeded) },
        { PerformedDropEffect, nameof(PerformedDropEffect) },
        { PreferredDropEffect, nameof(PreferredDropEffect) },
        { DragLoop, nameof(DragLoop) },
        { FileName, nameof(FileName) },
        { FileNameW, nameof(FileNameW) },
        { ShellidList, nameof(ShellidList) },
        { FileDrop, nameof(FileDrop) },
        { AdbDrop, nameof(AdbDrop) },
    };

    private static Dictionary<short, AdbDataFormat> FormatObjects => new()
    {
        { DragImage, DragImage },
        { FileContents, FileContents },
        { FileDescriptor, FileDescriptor },
        { PasteSucceeded, PasteSucceeded },
        { PerformedDropEffect, PerformedDropEffect },
        { PreferredDropEffect, PreferredDropEffect },
        { DragLoop, DragLoop },
        { FileName, FileName },
        { FileNameW, FileNameW },
        { ShellidList, ShellidList },
        { FileDrop, FileDrop },
        { AdbDrop, AdbDrop },
    };

    public static AdbDataFormat GetFormat(short id) =>
        FormatObjects.TryGetValue(id, out var format) ? format : null;

    public static string GetFormatName(short id) =>
        DataFormatKeys.TryGetValue(id, out var format) ? format : null;

    public static short? GetFormatId(string name)
    {
        var format = DataFormatKeys.Where(kv => kv.Value == name);
        return format.Any() ? format.First().Key : null;
    }
}

public class AdbDataFormat(string name)
{
    public AdbDataFormat(short id) : this(null)
    {
        Id = id;
    }

    public string Name { get; } = name;

    public short Id { get; } = (short)DataFormats.GetDataFormat(name).Id;

    public static implicit operator short(AdbDataFormat self)
        => self.Id;

    public static implicit operator string(AdbDataFormat self)
        => self.Name;

    public TYMED tymed => this == AdbDataFormats.FileContents
        ? TYMED.TYMED_ISTREAM
        : TYMED.TYMED_HGLOBAL;
}
