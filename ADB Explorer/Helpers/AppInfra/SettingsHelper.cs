using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public static class SettingsHelper
{
    public static void ResetAppAction()
    {
        Process.Start(Environment.ProcessPath);
        Application.Current.Shutdown();
    }

    public static void ChangeDefaultPathAction()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false
        };
        if (Data.Settings.DefaultFolder != "")
            dialog.DefaultDirectory = Data.Settings.DefaultFolder;

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Data.Settings.DefaultFolder = dialog.FileName;
        }
    }

    public static void ChangeAdbPathAction()
    {
        var dialog = new OpenFileDialog()
        {
            Multiselect = false,
            Title = Strings.Resources.S_OVERRIDE_ADB_BROWSE,
            Filter = $"{Strings.Resources.S_ADB_EXECUTABLE}|adb.exe",
        };

        if (!string.IsNullOrEmpty(Data.Settings.ManualAdbPath))
        {
            try
            {
                var dir = Directory.GetParent(Data.Settings.ManualAdbPath);

                if (dir.Exists)
                    dialog.InitialDirectory = dir.FullName;
            }
            catch (Exception) { }
        }

        if (dialog.ShowDialog() == true)
        {
            string message = "";
            ADBService.VerifyAdbVersion(dialog.FileName);
            if (Data.RuntimeSettings.AdbVersion is null)
            {
                message = Strings.Resources.S_MISSING_ADB_OVERRIDE;
            }
            else if (Data.RuntimeSettings.AdbVersion < AdbExplorerConst.MIN_ADB_VERSION)
            {
                message = Strings.Resources.S_ADB_VERSION_LOW_OVERRIDE;
            }

            if (message != "")
            {
                DialogService.ShowMessage(message, Strings.Resources.S_FAIL_OVERRIDE_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
                return;
            }

            Data.Settings.ManualAdbPath = dialog.FileName;
        }
    }

    public static IEnumerable<CultureInfo> GetAvailableLanguages()
    {
        string assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        yield return CultureInfo.InvariantCulture;
        yield return new CultureInfo("en-US");

        foreach (var dir in Directory.GetDirectories(AppDomain.CurrentDomain.BaseDirectory))
        {
            string folderName = Path.GetFileName(dir);
            CultureInfo culture = null;
            string resourceAssembly = "";

            try
            {
                // Attempt to create a CultureInfo from folder name
                culture = new(folderName);

                // Check if satellite assembly exists for this culture
                resourceAssembly = Path.Combine(dir, $"{assemblyName}.resources.dll");
                
            }
            catch (CultureNotFoundException)
            {
                // Folder name is not a valid culture
                continue;
            }

            if (File.Exists(resourceAssembly))
            {
                yield return culture;
            }
        }
    }

    public static double GetCurrentPercentageTranslated(CultureInfo currentCulture)
    {
        var neutralCulture = CultureInfo.InvariantCulture;

        var resourceManager = Strings.Resources.ResourceManager;
        var resourceType = typeof(Strings.Resources);
        var propertyInfos = resourceType.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        var stringProps = propertyInfos.Where(p => p.PropertyType == typeof(string));
        var neutralValues = stringProps.Select(p => resourceManager.GetString(p.Name, neutralCulture)).Where(s => !s.All(c => char.IsAsciiLetterUpper(c)));
        var currentValues = stringProps.Select(p => resourceManager.GetString(p.Name, currentCulture));
        double translated = neutralValues.Except(currentValues).Count();
        double total = neutralValues.Count();

        return translated / total;
    }
}
