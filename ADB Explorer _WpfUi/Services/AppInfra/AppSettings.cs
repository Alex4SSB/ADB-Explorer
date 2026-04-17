using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;

namespace ADB_Explorer.Services;

public partial class AppSettings : ObservableObject, IJsonOnDeserialized, IJsonOnSerializing
{
    public enum SystemVals
    {
        WindowMaximized,
        DetailedVisible,
        DetailedHeight,
    }

    public enum AppTheme
    {
        WindowsDefault,
        Light,
        Dark,
    }

    public enum DoubleClickAction
    {
        Pull,
        Edit,
        None,
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

    void IJsonOnDeserialized.OnDeserialized()
    {
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
    public partial bool SpecialFolderIcons { get; set; } = true;

    [ObservableProperty]
    public partial bool EnableBusyBox { get; set; } = false;

    [ObservableProperty]
    public partial bool EnableWsa { get; set; } = false;

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

    [JsonIgnore]
    public bool IsPullOnDoubleClickEnabled => !string.IsNullOrEmpty(DefaultFolder);

    [ObservableProperty]
    public partial string ManualAdbPath { get; set; } = "";

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
    public partial DoubleClickAction DoubleClick { get; set; } = DoubleClickAction.None;

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

    #endregion

    #region File Ops

    [ObservableProperty]
    public partial bool EnableCompactView { get; set; } = false;

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

    [field: JsonIgnore]
    [ObservableProperty]
    public partial bool IsAnimated { get; set; } = true;

    //private bool disableAnimation = false;
    /// <summary>
    /// Disables all visual animations
    /// </summary>
    public bool DisableAnimation
    {
        get
        {
            //if (!Data.RuntimeSettings.IsWindowLoaded)
            //    IsAnimated = !disableAnimation;

            //return disableAnimation;

            return false;
        }
        //set => SetProperty(ref disableAnimation, value);
    }

    [ObservableProperty]
    public partial bool EnableSplash { get; set; } = true;

    #endregion

    [ObservableProperty]
    public partial AppTheme Theme { get; set; } = AppTheme.WindowsDefault;

    [ObservableProperty]
    public partial bool HidePasteNamingInfo { get; set; } = false;
    public string LastVersion { get; set; } = "0.0.0";

    [ObservableProperty]
    public partial bool ShowLaunchWsaMessage { get; set; } = true;
    public long EditorMaxFileSize { get; set; } = 300_000;

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

    private void UpdateTranslation()
    {
        CultureInfo actual = UICulture.Equals(CultureInfo.InvariantCulture)
            ? OriginalCulture
            : UICulture;

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
                    originalCulture = Thread.CurrentThread.CurrentUICulture;
                }
                catch
                { }
            }
            return originalCulture;
        }
    }

    [ObservableProperty]
    public partial bool ShowLanguageNotification { get; set; } = true;

    [ObservableProperty]
    public partial bool HomeLocationsExpanded { get; set; } = false;
}
