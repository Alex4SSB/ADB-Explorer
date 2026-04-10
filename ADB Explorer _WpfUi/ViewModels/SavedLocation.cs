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
            () => Data.Settings.SavedLocations.Remove(Path));

        AddAction = new(
            () => string.IsNullOrEmpty(Path),
            () => Data.Settings.SavedLocations.Add(Data.CurrentPath));

        NavigateAction = new(
            () => !string.IsNullOrEmpty(Path),
            () => Data.RuntimeSettings.LocationToNavigate = new(Path));
    }
}
