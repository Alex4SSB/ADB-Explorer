using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Models;

public interface IFileStat
{
    public FileType Type { get; set; }

    public ulong? Size { get; set; }

    public DateTime? ModifiedTime { get; set; }

    public bool IsLink { get; set; }
}

public interface IBaseFile
{
    public string FullName { get; }

    public string FullPath { get; }
}
