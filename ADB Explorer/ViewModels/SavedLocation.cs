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

    private static string? CurrentDeviceId => Data.DevicesObject?.Current?.ID;

    public SavedLocation(string path = "")
    {
        Path = path;

        DeleteAction = new(
            () => !string.IsNullOrEmpty(Path),
            () =>
            {
                var entry = Data.Settings.SavedLocations.FirstOrDefault(e => e.DeviceId == CurrentDeviceId && e.Path == Path);
                if (entry is not null)
                    Data.Settings.SavedLocations.Remove(entry);
            });

        AddAction = new(
            () => string.IsNullOrEmpty(Path) && CurrentDeviceId is not null,
            () => Data.Settings.SavedLocations.Add(new(CurrentDeviceId, Data.CurrentPath)));

        NavigateAction = new(
            () => !string.IsNullOrEmpty(Path),
            () => Data.RuntimeSettings.LocationToNavigate = new(Path));
    }
}
