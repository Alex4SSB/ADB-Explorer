using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Models;

/// <summary>
/// Represents the basic properties returned by the `stat` command on Android devices.
/// </summary>
public interface IFileStat
{
    public FileType Type { get; set; }

    public long? Size { get; set; }

    public DateTime? ModifiedTime { get; set; }

    public bool IsLink { get; set; }
}

/// <summary>
/// Represents the basic properties of a file or directory.
/// </summary>
public interface IBaseFile
{
    public string FullName { get; }

    public string FullPath { get; }
}

/// <summary>
/// Represents the items that can be displayed in the browser - <see cref="FileClass"/> and <see cref="Package"/>.
/// </summary>
public interface IBrowserItem
{
    public string DisplayName { get; }
}
