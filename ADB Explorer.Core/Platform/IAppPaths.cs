namespace ADB_Explorer.Platform;

/// <summary>
/// OS-specific locations the app reads and writes.
/// </summary>
public interface IAppPaths
{
    /// <summary>Default per-user folder for settings, caches and logs.</summary>
    string DefaultAppDataFolder { get; }

    /// <summary>Folder for disposable files, such as files pulled before a drag.</summary>
    string TempFolder { get; }
}
