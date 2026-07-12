using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public partial class AppSettings : ObservableObject, IJsonOnDeserialized, IJsonOnSerializing
{
    public enum AppTheme
    {
        WindowsDefault,
        Light,
        Dark,
    }

    public enum ThumbnailMode
    {
        Off,
        IconViewOnly,
        OnPhotoDir,
        OnConnect,
    }

    public enum ThumbnailAge
    {
        Disabled,
        OneMonth,
        OneWeek,
        OneDay,
        OneHour,
    }

    public enum FileSizeDisplay
    {
        B,
        K,
        KM,
        KMG,
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        _locationThumbSize ??= [];
        _locationSorting ??= [];
        SavedLocations ??= [];

        LocationThumbSize = _locationThumbSize;
        LocationSorting = _locationSorting;
    }

    void IJsonOnSerializing.OnSerializing()
    {
        _locationThumbSize = LocationThumbSize.Where(kv => kv.Value is not ThumbnailService.ThumbnailSize.Disabled).ToDictionary();
        _locationSorting = LocationSorting.Where(kv => kv.Value.Property != SortingSelector.SortingProperty.Name || kv.Value.Direction != ListSortDirection.Ascending).ToDictionary();
    }

    [ObservableProperty]
    public partial ThumbnailAge ThumbsAge { get; set; } = ThumbnailAge.Disabled;

    [ObservableProperty]
    public partial ThumbnailMode ThumbsMode { get; set; } = ThumbnailMode.Off;

    [ObservableProperty]
    public partial bool MovieThumbsEnabled { get; set; } = false;

    [ObservableProperty]
    public partial bool PersistThumbs { get; set; } = true;

    [ObservableProperty]
    public partial bool LimitThumbsPullSpeed { get; set; } = true;

    [ObservableProperty]
    public partial bool ThumbSizePerLocation { get; set; } = true;

    [JsonIgnore]
    [ObservableProperty]
    public partial Dictionary<string, ThumbnailService.ThumbnailSize> LocationThumbSize { get; set; } = [];

    public Dictionary<string, ThumbnailService.ThumbnailSize> _locationThumbSize { get; set; } = [];

    [ObservableProperty]
    public partial int MaxCustomThumbWeight { get; set; } = 0;

    [ObservableProperty]
    public partial bool SortingPerLocation { get; set; } = true;

    [JsonIgnore]
    [ObservableProperty]
    public partial Dictionary<string, SortingSelector.DirSortingOption> LocationSorting { get; set; } = [];

    public Dictionary<string, SortingSelector.DirSortingOption> _locationSorting { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<string> SavedLocations { get; set; } = [];

    [ObservableProperty]
    public partial StorageDevice[] StorageDevices { get; set; }

    [ObservableProperty]
    public partial bool SpecialFolderIcons { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableBusyBox { get; set; } = false;

    [ObservableProperty]
    public partial bool EnableWsa { get; set; } = false;

    [ObservableProperty]
    public partial bool EnableEmulatorDiscovery { get; set; } = false;

    [ObservableProperty]
    public partial bool IsDetailsPaneOpen { get; set; } = false;

    [ObservableProperty]
    public partial int DetailsPaneWidth { get; set; } = 300;

    [ObservableProperty]
    public partial DetailsPane.SidePaneMode SidePane { get; set; } = DetailsPane.SidePaneMode.Details;

    [ObservableProperty]
    public partial int MaxPreviewFileSize { get; set; } = 300;

    [ObservableProperty]
    public partial bool WindowMaximized { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowMessageOnCrash { get; set; } = true;

    [ObservableProperty]
    public partial FileSizeDisplay FileSizeMode { get; set; } = FileSizeDisplay.KMG;

    [ObservableProperty]
    public partial int FileSizeDecimal { get; set; } = 1;

    #region paths

    private string defaultFolder = "";
    /// <summary>
    /// Default Folder For Pull On Double-Click And Folder / File Browsers
    /// </summary>
    public string DefaultFolder
    {
        get => defaultFolder;
        set
        {
            if (SetProperty(ref defaultFolder, value))
                OnPropertyChanged(nameof(IsPullOnDoubleClickEnabled));
        }
    }

    // Require the folder to still exist: a stale DefaultFolder (deleted/unmounted after being set) would
    // otherwise make double-click-to-pull fire at a dead path and fail.
    [JsonIgnore]
    public bool IsPullOnDoubleClickEnabled => !string.IsNullOrEmpty(DefaultFolder) && Directory.Exists(DefaultFolder);

    [ObservableProperty]
    public partial string ManualAdbPath { get; set; } = "";

    private bool _disableAdbRestrictionsLoaded;
    private bool _disableAdbRestrictions;
    private bool _disableAdbRestrictionsActive;

    /// <summary>
    /// Whether ADB security restrictions are disabled for this session. Defaults to the safe state
    /// (restrictions enabled) and is set only at launch, once the vault load completes.
    /// </summary>
    [JsonIgnore]
    public bool DisableAdbRestrictionsActive => _disableAdbRestrictionsActive;

    /// <summary>
    /// Loads vault-backed settings on a background thread so a slow or unresponsive Credential Vault
    /// cannot freeze startup (issue #329). Until this completes the app stays in the safe, restricted
    /// state. If the feature turns out to be enabled but ADB was already rejected while the value was
    /// still unknown, ADB verification is re-run so a legitimately-allowed ADB is accepted.
    /// </summary>
    public Task LoadVaultSettingsAsync() => Task.Run(() =>
    {
        if (!CredentialVaultStore.TryGet(nameof(DisableAdbRestrictions), out var value))
            return; // Vault unavailable/slow: keep safe defaults and try again on the next launch.

        bool stored = value == "True";

        // The session ("active") value always reflects what the vault held at launch, even if the user
        // has already toggled the pending setting this session (that change only applies after restart).
        _disableAdbRestrictionsActive = stored;

        // Adopt the stored value as the current setting only if the user has not changed it yet.
        if (!_disableAdbRestrictionsLoaded)
        {
            _disableAdbRestrictions = stored;
            _disableAdbRestrictionsLoaded = true;
            App.SafeInvoke(() => OnPropertyChanged(nameof(DisableAdbRestrictions)));
        }

        // The initial verification may have run under the default restricted state and flagged a
        // user-approved ADB as invalid. Now that the setting is known, re-evaluate.
        if (_disableAdbRestrictionsActive
            && AdbHelper.CurrentAdbState.Status is not AdbHelper.AdbStatus.Valid)
        {
            _ = AdbHelper.CheckAdbVersion();
        }
    });

    public void ClearVaultSettings()
    {
        _disableAdbRestrictions = false;
        _disableAdbRestrictionsActive = false;
        _disableAdbRestrictionsLoaded = true;
    }

    /// <summary>
    /// Persists vault-backed settings. Called on normal app exit; the vault write is timeout-protected
    /// so it cannot hang shutdown even on an unresponsive vault.
    /// </summary>
    public void PersistVaultSettings()
    {
        if (!_disableAdbRestrictionsLoaded)
            return;

        CredentialVaultStore.Set(nameof(DisableAdbRestrictions), _disableAdbRestrictions ? "True" : "False");
    }

    [JsonIgnore]
    public bool DisableAdbRestrictions
    {
        get => _disableAdbRestrictions;

        set
        {
            if (_disableAdbRestrictionsLoaded && _disableAdbRestrictions == value)
                return;

            _disableAdbRestrictions = value;
            _disableAdbRestrictionsLoaded = true;
            OnPropertyChanged();
        }
    }

    #endregion

    #region File Behavior

    [ObservableProperty]
    public partial bool ShowExtensions { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSystemPackages { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowHiddenItems { get; set; } = true;

    #endregion

    #region Double Click

    [ObservableProperty]
    public partial bool DoubleClickToPull { get; set; } = false;

    #endregion

    #region device

    [ObservableProperty]
    public partial bool SaveDevices { get; set; } = true;

    /// <summary>
    /// Automatically Open Device For Browsing
    /// </summary>
    [ObservableProperty]
    public partial bool AutoOpen { get; set; } = false;

    /// <summary>
    /// Automatically Try To Open Devices Using Root Privileges
    /// </summary>
    [ObservableProperty]
    public partial bool AutoRoot { get; set; } = false;

    #endregion

    #region Drives & Features

    [ObservableProperty]
    public partial bool PollDrives { get; set; } = true;

    /// <summary>
    /// Enable moving deleted items to a special folder, instead of permanently deleting them
    /// </summary>
    [ObservableProperty]
    public partial bool EnableRecycle { get; set; } = false;

    [ObservableProperty]
    public partial bool EnableApk { get; set; } = false;

    #endregion

    #region adb

    /// <summary>
    /// Runs ADB server with mDNS enabled, polling for services is enabled in the connection expander
    /// </summary>
    [ObservableProperty]
    public partial bool EnableMdns { get; set; } = true;

    /// <summary>
    /// <see langword="false"/> - disables polling for both ADB devices and mDNS services. Enables manual refresh button
    /// </summary>
    [ObservableProperty]
    public partial bool PollDevices { get; set; } = true;

    /// <summary>
    /// Enables battery information for all devices (polling and displaying)
    /// </summary>
    [ObservableProperty]
    public partial bool PollBattery { get; set; } = true;

    /// <summary>
    /// <see langword="false"/> - disables logging of commands and command log button
    /// </summary>
    [ObservableProperty]
    public partial bool EnableLog { get; set; } = false;

    [ObservableProperty]
    public partial bool KillAdbOnExit { get; set; } = true;

    #endregion

    #region File Ops

    [ObservableProperty]
    public partial FileOpColumnState[] FileOpColumns { get; set; } = [];

    [ObservableProperty]
    public partial FileOpFilter.FilterType[] FileOpFilters { get; set; } = [];

    [ObservableProperty]
    public partial bool StopPollingOnSync { get; set; } = false;

    [ObservableProperty]
    public partial bool AllowMultiOp { get; set; } = true;

    [ObservableProperty]
    public partial bool RescanOnPush { get; set; } = true;

    [ObservableProperty]
    public partial bool KeepDateModified { get; set; } = true;

    #endregion

    #region about

    /// <summary>
    /// GET releases on GitHub repo on each launch
    /// </summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; } = true;

    #endregion

    #region graphics

    /// <summary>
    /// Disables HW acceleration
    /// </summary>
    [ObservableProperty]
    public partial bool SwRender { get; set; } = false;

    [ObservableProperty]
    public partial bool UseCustomAccent { get; set; } = false;

    [ObservableProperty]
    public partial string? AccentColorHex { get; set; } = null;

    [JsonIgnore]
    public Color AccentColor
    {
        get
        {
            if (AccentColorHex is { } hex)
                return (Color)ColorConverter.ConvertFromString(hex);
            var sys = ApplicationAccentColorManager.SystemAccent;
            return sys == Colors.Transparent
                ? ApplicationAccentColorManager.GetColorizationColor()
                : sys;
        }
        set => AccentColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    partial void OnAccentColorHexChanged(string? value) => OnPropertyChanged(nameof(AccentColor));
    partial void OnUseCustomAccentChanged(bool value) => OnPropertyChanged(nameof(AccentColor));

    #endregion

    [ObservableProperty]
    public partial AppTheme Theme { get; set; } = AppTheme.WindowsDefault;

    public string LastVersion { get; set; } = "0.0.0";

    public string? PrivacyCheckAppVersion { get; set; }

    public DateTime? PendingPrivacyUpdate { get; set; }

    public DateTime? LastAcknowledgedPrivacyUpdate { get; set; }

    [ObservableProperty]
    public partial bool ShowLaunchWsaMessage { get; set; } = true;
    
    public bool? UnrootOnDisconnect { get; set; } = null;

    public string? LastDevice { get; set; } = null;

    [JsonIgnore]
    private CultureInfo? uiCulture;
    [JsonIgnore]
    public CultureInfo UICulture
    {
        get
        {
            if (uiCulture is null)
            {
                var uiLanguage = UILanguage;
                uiCulture = string.IsNullOrEmpty(uiLanguage)
                    ? CultureInfo.InvariantCulture
                    : new(uiLanguage);

                UpdateTranslation();
            }

            return uiCulture;
        }
        set
        {
            if (SetProperty(ref uiCulture, value))
            {
                UILanguage = value.Name;
                UpdateTranslation();
            }
        }
    }

    [JsonIgnore]
    public CultureInfo ActualUICulture => UICulture.Equals(CultureInfo.InvariantCulture) ? OriginalUICulture : UICulture;

    /// <summary>Culture used for date, time, and number formatting. Always Windows' regional format when no explicit app language is set.</summary>
    [JsonIgnore]
    public CultureInfo ActualFormatCulture => UICulture.Equals(CultureInfo.InvariantCulture) ? OriginalCulture : UICulture;

    private void UpdateTranslation()
    {
        CultureInfo actual = UICulture.Equals(CultureInfo.InvariantCulture)
            ? OriginalUICulture
            : UICulture;

        // Set the static resource culture so lookups are correct regardless of which
        // thread accesses the resource strings (e.g. context menus outside the visual tree).
        Strings.Resources.Culture = actual;

        AppCulture.ApplyThreadCultures();

        string? percent = actual.Name == "en" || actual.Parent.Name == "en"
                         ? null
                         : $"\u200E{SettingsHelper.GetCurrentPercentageTranslated(actual) * 100:f0}%";

        CultureTranslationProgress.Value = percent;
    }

    [JsonIgnore]
    public ObservableProperty<string> CultureTranslationProgress { get; private set; } = new() { Value = null };

    [ObservableProperty]
    public partial string UILanguage { get; set; } = "";

    [JsonIgnore]
    private CultureInfo? originalCulture = null;
    [JsonIgnore]
    public CultureInfo OriginalCulture
    {
        get
        {
            if (originalCulture is null)
            {
                try
                {
                    originalCulture = CultureInfo.CurrentCulture;
                }
                catch
                { }
            }
            return originalCulture;
        }
    }

    [JsonIgnore]
    private CultureInfo? originalUICulture = null;
    /// <summary>Windows display language, used as the default app UI language.</summary>
    [JsonIgnore]
    public CultureInfo OriginalUICulture
    {
        get
        {
            if (originalUICulture is null)
            {
                try
                {
                    originalUICulture = CultureInfo.CurrentUICulture;
                }
                catch
                { }
            }
            return originalUICulture;
        }
    }

    [ObservableProperty]
    public partial bool ShowLanguageNotification { get; set; } = true;

    [ObservableProperty]
    public partial bool HomeLocationsExpanded { get; set; } = false;
}
