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

        // Set the mDNS flag before starting the vault load: that load may re-evaluate ADB on a background
        // thread (running "adb version", which can start the ADB server). Assigning it first ensures the
        // Task.Run happens-before edge makes the correct value visible, so a server we start carries mDNS.
        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;

        // Load vault-backed settings off the UI thread; a slow/unresponsive vault must not block startup.
        _ = Data.Settings.LoadVaultSettingsAsync();
    }

    public void Save()
    {
        SaveSettingsFile();
        Data.Settings.PersistVaultSettings();
    }

    /// <summary>
    /// Writes <c>settings.json</c> only. Use when the settings file must hit disk before a restart
    /// without waiting on the Credential Vault (which can block for seconds when unresponsive).
    /// </summary>
    public void SaveSettingsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Data.Settings, _options));
    }

    public void DeleteSettingsFile()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
