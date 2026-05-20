namespace ADB_Explorer.Models;

public partial class VirtualDrive : Drive
{
    [ObservableProperty]
    public partial long? ItemsCount { get; set; } = 0;

    public override bool IsFUSE => false;

    public VirtualDrive(string path = "", long itemsCount = 0) : base(path)
    {
        ItemsCount = itemsCount;
    }
}
