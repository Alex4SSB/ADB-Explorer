namespace ADB_Explorer.Models;

public partial class VirtualDrive : Drive
{
    [ObservableProperty]
    public partial long? ItemsCount { get; set; } = 0;

    /// <summary>Filesystem mount point from <c>df</c> (e.g. <c>/data</c> for <c>/data/local/tmp</c>).</summary>
    public string? MountPoint { get; set; }

    public VirtualDrive(string path = "", long itemsCount = 0) : base(path)
    {
        ItemsCount = itemsCount;
    }
}
