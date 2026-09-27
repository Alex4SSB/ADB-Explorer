namespace ADB_Explorer.ViewModels;

public class SavedLocation : ObservableObject
{
    private string _path = "";
    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public BaseAction DeleteAction { get; }

    public BaseAction AddAction { get; }

    public BaseAction NavigateAction { get; }

    /// <summary>The device this entry was listed for; the "add current location" placeholder
    /// has none and acts on whichever tab is active when it's clicked.</summary>
    private readonly string? _ownerDeviceId;

    private string? DeviceId => _ownerDeviceId ?? Data.ActiveDevice?.ID;

    public SavedLocation(string path = "", string? deviceId = null)
    {
        Path = path;
        _ownerDeviceId = deviceId;

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
            () => Data.RequestNavigation(new(Path)));
    }
}
