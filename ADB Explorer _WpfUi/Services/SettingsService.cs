using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class SettingsService
{
    private readonly string _path = Data.AppDataPath is null
        ? string.Empty
        : Path.Combine(Data.AppDataPath, "settings.json");

    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true
    };

    public void Load()
    {
        if (!File.Exists(_path))
            return;

        var json = File.ReadAllText(_path);
        Data.Settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        
        File.WriteAllText(_path, JsonSerializer.Serialize(Data.Settings, _options));
    }
}
