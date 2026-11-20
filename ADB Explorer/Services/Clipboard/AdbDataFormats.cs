using System.Runtime.InteropServices.ComTypes;

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
    public static AdbDataFormat DropDescription { get; } = new("DropDescription");
    public static AdbDataFormat CanIncludeInClipboardHistory { get; } = new("CanIncludeInClipboardHistory");
    public static AdbDataFormat CanUploadToCloudClipboard { get; } = new("CanUploadToCloudClipboard");
    public static AdbDataFormat FileName { get; } = new("FileName");
    public static AdbDataFormat FileNameW { get; } = new("FileNameW");
    public static AdbDataFormat ShellidList { get; } = new("Shell IDList Array");
    public static AdbDataFormat FileDrop { get; } = new(DataFormats.FileDrop);
    public static AdbDataFormat AdbDrop { get; } = new(AdbExplorerConst.ADB_DRAG_FORMAT);
    public static AdbDataFormat DeviceIndependentBitmap { get; } = new(DataFormats.Dib);
    /// <summary>
    /// CF_DIBV5 (Format17), used for clipboard images
    /// </summary>
    public static AdbDataFormat Format17 { get; } = new(17);
}

/// <summary>
/// A registered clipboard format. Equal by its id, so one made from a target's cfFormat matches ours.
/// </summary>
public sealed class AdbDataFormat : IEquatable<AdbDataFormat>
{
    public AdbDataFormat(string name)
    {
        Name = name;
        Id = (short)DataFormats.GetDataFormat(name).Id;
    }

    public AdbDataFormat(short id)
    {
        Id = id;
        Name = DataFormats.GetDataFormat(id).Name;
    }

    public string Name { get; } = "";

    public short Id { get; }

    public static implicit operator uint(AdbDataFormat self)
        => (uint)self.Id;

    public static implicit operator short(AdbDataFormat self)
        => self.Id;

    public static implicit operator string(AdbDataFormat self)
        => self.Name;

    public TYMED tymed => this == AdbDataFormats.FileContents
        ? TYMED.TYMED_ISTREAM
        : TYMED.TYMED_HGLOBAL;

    public bool Equals(AdbDataFormat? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as AdbDataFormat);

    public override int GetHashCode() => Id;

    public static bool operator ==(AdbDataFormat? left, AdbDataFormat? right) => left?.Equals(right) ?? right is null;

    public static bool operator !=(AdbDataFormat? left, AdbDataFormat? right) => !(left == right);
}
