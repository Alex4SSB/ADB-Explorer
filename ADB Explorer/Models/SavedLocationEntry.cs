namespace ADB_Explorer.Models;

/// <summary>
/// A saved location scoped to the device that owns it. <see cref="DeviceId"/> is <see langword="null"/>
/// only for entries migrated from the pre-multi-device flat path list, until the first device to
/// connect this session claims them (see <see cref="ADB_Explorer.ViewModels.Devices"/>).
/// </summary>
public partial class SavedLocationEntry : ObservableObject
{
    [ObservableProperty]
    public partial string? DeviceId { get; set; }

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    public SavedLocationEntry()
    { }

    public SavedLocationEntry(string? deviceId, string path)
    {
        DeviceId = deviceId;
        Path = path;
    }
}
