using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Helpers;

public static class AppDataHelper
{
    private const string AppDataLocationVaultKey = "AppDataLocation";
    private const string DefaultAppDataMarker = "[DEFAULT]";

    public static bool IsAppDataLocationChoiceMade() => CredentialVaultStore.Exists(AppDataLocationVaultKey);

    /// <summary>
    /// Resolves the app data folder for the current launch (portable default or packaged virtualized/custom).
    /// </summary>
    public static string ResolveAppDataPath()
    {
        if (!Data.RuntimeSettings.IsAppPackaged)
        {
            return Path.Combine(
                global::Windows.Storage.UserDataPaths.GetDefault().LocalAppData,
                AdbExplorerConst.APP_DATA_FOLDER);
        }

        var customPath = GetCustomAppDataPath();
        if (customPath is not null)
            return customPath;

        return GetVirtualizedAppDataPath();
    }

    private static string? GetCustomAppDataPath()
    {
        var value = CredentialVaultStore.Get(AppDataLocationVaultKey);
        return value is null || value == DefaultAppDataMarker ? null : value;
    }

    private static void SaveAppDataLocationChoice(string value)
        => CredentialVaultStore.Set(AppDataLocationVaultKey, value);

    /// <summary>
    /// Shows the app data location dialog. Returns a custom path when selected, otherwise null.
    /// </summary>
    public static async Task<string?> PromptAppDataLocationChoiceAsync()
    {
        while (true)
        {
            var result = await DialogService.ShowDialog(
                Strings.Resources.S_APP_DATA_LOCATION_MESSAGE,
                Strings.Resources.S_APP_DATA_LOCATION_TITLE,
                Strings.Resources.S_APP_DATA_LOCATION_VIRTUALIZED,
                Strings.Resources.S_APP_DATA_LOCATION_CUSTOM,
                Strings.Resources.S_CANCEL);

            if (result == ContentDialogResult.Primary)
            {
                SaveAppDataLocationChoice(DefaultAppDataMarker);
                return null;
            }

            if (result == ContentDialogResult.Secondary)
            {
                var folder = PickCustomAppDataFolder();
                if (folder is null)
                    continue;

                Directory.CreateDirectory(folder);
                MigrateToCustomLocation(GetVirtualizedAppDataPath(), folder);
                SaveAppDataLocationChoice(folder);
                return folder;
            }

            return null;
        }
    }

    public static void ApplyAppDataPath(string appDataPath, IServiceProvider services)
    {
        Data.AppDataPath = appDataPath;
        var settingsPath = FileHelper.ConcatPaths(appDataPath, AdbExplorerConst.APP_SETTINGS_FILE, '\\');
        services.GetRequiredService<SettingsService>().Load(settingsPath);
    }

    public static string GetVirtualizedAppDataPath()
    {
        var familyName = NativeMethods.GetCurrentPackageFamilyName();
        if (familyName is not null)
        {
            return Path.Combine(
                global::Windows.Storage.UserDataPaths.GetDefault().LocalAppData,
                "Packages", familyName, "LocalCache", "Local", AdbExplorerConst.APP_DATA_FOLDER);
        }

        return Path.Combine(
            global::Windows.Storage.UserDataPaths.GetDefault().LocalAppData,
            AdbExplorerConst.APP_DATA_FOLDER);
    }

    private static string? PickCustomAppDataFolder()
    {
        var dialog = new CommonOpenFileDialog
        {
            IsFolderPicker = true,
            Multiselect = false,
            AllowNonFileSystemItems = false,
            DefaultDirectory = "C:\\",
            EnsurePathExists = false,
            Title = Strings.Resources.S_APP_DATA_LOCATION_BROWSE,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return null;

        string appData = FileHelper.GetParentPath(Windows.Storage.UserDataPaths.GetDefault().LocalAppData);
        if (dialog.FileName.StartsWith(appData))
        {
            DialogService.ShowMessage(Strings.Resources.S_CUSTOM_LOCATION_INVALID, 
                Strings.Resources.S_APP_DATA_LOCATION_TITLE,
                DialogService.DialogIcon.Exclamation);

            return null;
        }

        return Path.GetFullPath(dialog.FileName);
    }

    private static void MigrateToCustomLocation(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath))
            return;

        try
        {
            MergeDirectory(sourcePath, targetPath);

            if (Directory.Exists(sourcePath))
                TryDeleteDirectory(sourcePath);
        }
        catch
        { }
    }

    private static void MergeDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(targetDir))
        {
            Directory.Move(sourceDir, targetDir);
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetFile = Path.Combine(targetDir, fileName);

            if (ShouldCopyFile(sourceFile, targetFile))
                File.Copy(sourceFile, targetFile, overwrite: true);
        }

        foreach (var sourceSubDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(sourceSubDir);
            MergeDirectory(sourceSubDir, Path.Combine(targetDir, dirName));
        }
    }

    private static bool ShouldCopyFile(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
            return true;

        if (string.Equals(Path.GetFileName(sourceFile), AdbExplorerConst.APP_SETTINGS_FILE, StringComparison.OrdinalIgnoreCase))
            return File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(targetFile);

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        { }
    }
}
