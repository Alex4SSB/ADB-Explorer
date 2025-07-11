using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class SavedLocation : ViewModelBase
{
    private string path;
    public string Path
    {
        get => path;
        set => Set(ref path, value);
    }

    public BaseAction DeleteAction { get; }

    public BaseAction AddAction { get; }

    public BaseAction NavigateAction { get; }

    public SavedLocation(string path = "")
    {
        Path = path;

        DeleteAction = new(
            () => !string.IsNullOrEmpty(Path),
            () =>
            {
                Data.RuntimeSettings.SavedLocations = [.. Data.RuntimeSettings.SavedLocations.Except([Path])];
                //Data.RuntimeSettings.SavedLocations.Remove(Path);
                Storage.StoreValue(nameof(Data.RuntimeSettings.SavedLocations), Data.RuntimeSettings.SavedLocations.ToArray());
            });

        AddAction = new(
            () => string.IsNullOrEmpty(Path),
            () =>
            {
                Data.RuntimeSettings.SavedLocations = [.. Data.RuntimeSettings.SavedLocations, Data.CurrentPath];
                //Data.RuntimeSettings.SavedLocations.Add(Data.CurrentPath);
                Storage.StoreValue(nameof(Data.RuntimeSettings.SavedLocations), Data.RuntimeSettings.SavedLocations.ToArray());
            });

        NavigateAction = new(
            () => !string.IsNullOrEmpty(Path),
            () => Data.RuntimeSettings.LocationToNavigate = new(Path));
    }
}
