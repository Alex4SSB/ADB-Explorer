namespace ADB_Explorer.Models;

public class FileStat : AbstractFile, IBaseFile, IFileStat
{
    public FileStat(string fileName,
                    string path,
                    FileType type,
                    bool isLink,
                    ulong? size,
                    DateTime? modifiedTime)
    {
        FullName = fileName;
        FullPath = path;

        Type = type;
        Size = size;
        ModifiedTime = modifiedTime;

        IsLink = isLink;
    }

    public string FullName { get; set; }

    public string FullPath { get; set; }

    public FileType Type { get; set; }

    public ulong? Size { get; set; }

    public DateTime? ModifiedTime { get; set; }

    public bool IsLink { get; set; }
}
