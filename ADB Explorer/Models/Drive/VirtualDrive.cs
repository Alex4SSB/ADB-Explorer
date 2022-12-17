namespace ADB_Explorer.Models;

public class VirtualDrive : Drive
{
    private long itemsCount = 0;
    public long ItemsCount
    {
        get => itemsCount;
        set => Set(ref itemsCount, value);
    }

    public VirtualDrive(string path = "", long itemsCount = 0) : base(path)
    {
        ItemsCount = itemsCount;
    }
}
