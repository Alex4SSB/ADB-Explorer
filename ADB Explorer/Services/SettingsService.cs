using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class SettingsService
{
    private string _path = "";

    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true
    };

    public void Load(string settingsPath, string oldFile = "")
    {
        _path = settingsPath;

        if (!File.Exists(_path))
        {
            Data.Settings = new AppSettings();

            if (!string.IsNullOrEmpty(oldFile) && File.Exists(oldFile))
            {
                var oldSettings = File.ReadAllText(oldFile);
                var s1 = oldSettings.Split("ManualAdbPath:\"");
                if (s1.Length > 1)
                {
                    var adbPath = s1[1].Split("\";")[0];
                    if (adbPath.Length > 0)
                    {
                        Data.Settings.ManualAdbPath = adbPath;
                    }
                }

                File.Delete(oldFile);
            }
        }
        else
        {
            var json = File.ReadAllText(_path);
            Data.Settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
        }

        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        
        File.WriteAllText(_path, JsonSerializer.Serialize(Data.Settings, _options));
    }
}
