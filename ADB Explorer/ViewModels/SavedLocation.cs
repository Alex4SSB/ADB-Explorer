using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public class SavedLocation : ViewModelBase
{
    private string path = "";
    public string Path
    {
        get => path;
        set => Set(ref path, value);
    }

    public BaseAction DeleteAction { get; }

    public BaseAction AddAction { get; }

    public BaseAction NavigateAction { get; }

    /// <summary>The device this entry was listed for; the "add current location" placeholder
    /// has none and acts on whichever tab is active when it's clicked.</summary>
    private readonly string? ownerDeviceId;

    private string? DeviceId => ownerDeviceId ?? Data.ActiveDevice?.ID;

    public SavedLocation(string path = "", string? deviceId = null)
    {
        Path = path;
        ownerDeviceId = deviceId;

        DeleteAction = new(
            () => !string.IsNullOrEmpty(Path),
            () =>
            {
                var entry = Data.Settings.SavedLocations.FirstOrDefault(e => e.DeviceId == DeviceId && e.Path == Path);
                if (entry is not null)
                    Data.Settings.SavedLocations.Remove(entry);
            });

        AddAction = new(
            () => string.IsNullOrEmpty(Path) && DeviceId is not null,
            () => Data.Settings.SavedLocations.Add(new(DeviceId, Data.CurrentPath)));

        NavigateAction = new(
            () => !string.IsNullOrEmpty(Path),
            () => Data.RuntimeSettings.LocationToNavigate = new(Path));
    }
}
