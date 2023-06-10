namespace ADB_Explorer.Models;

public class FileStat : FilePath
{
    public FileStat(string fileName,
                    string path,
                    FileType type,
                    bool isLink = false,
                    ulong? size = null,
                    DateTime? modifiedTime = null)
        : base(path, fileName, type)
    {
        this.type = type;
        this.size = size;
        this.modifiedTime = modifiedTime;
        this.isLink = isLink;
    }

    private FileType type;
    public FileType Type
    {
        get => type;
        set => Set(ref type, value);
    }

    private ulong? size;
    public ulong? Size
    {
        get => size;
        set => Set(ref size, value);
    }

    private DateTime? modifiedTime;
    public virtual DateTime? ModifiedTime
    {
        get => modifiedTime;
        set => Set(ref modifiedTime, value);
    }

    private bool isLink;
    public bool IsLink
    {
        get => isLink;
        set => Set(ref isLink, value);
    }
}
