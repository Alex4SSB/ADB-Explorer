using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public partial class AppSettings : ObservableObject
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
    private string manualAdbPath = "";

    #endregion

    #region File Behavior

    [ObservableProperty]
    private bool showExtensions = true;

    [ObservableProperty]
    private bool showSystemPackages = true;

    [ObservableProperty]
    private bool showHiddenItems = true;

    #endregion

    #region Double Click

    [ObservableProperty]
    private DoubleClickAction doubleClick = DoubleClickAction.None;

    #endregion

    #region device

    [ObservableProperty]
    private bool saveDevices = true;

    /// <summary>
    /// Automatically Open Device For Browsing
    /// </summary>
    [ObservableProperty]
    private bool autoOpen = false;

    /// <summary>
    /// Automatically Try To Open Devices Using Root Privileges
    /// </summary>
    [ObservableProperty]
    private bool autoRoot = false;

    #endregion

    #region Drives & Features

    [ObservableProperty]
    private bool pollDrives = true;

    /// <summary>
    /// Enable moving deleted items to a special folder, instead of permanently deleting them
    /// </summary>
    [ObservableProperty]
    private bool enableRecycle = false;

    [ObservableProperty]
    private bool enableApk = false;

    #endregion

    #region adb

    /// <summary>
    /// Runs ADB server with mDNS enabled, polling for services is enabled in the connection expander
    /// </summary>
    [ObservableProperty]
    private bool enableMdns = true;

    /// <summary>
    /// <see langword="false"/> - disables polling for both ADB devices and mDNS services. Enables manual refresh button
    /// </summary>
    [ObservableProperty] 
    private bool pollDevices = true;

    /// <summary>
    /// Enables battery information for all devices (polling and displaying)
    /// </summary>
    [ObservableProperty] 
    private bool pollBattery = true;

    /// <summary>
    /// <see langword="false"/> - disables logging of commands and command log button
    /// </summary>
    [ObservableProperty] 
    private bool enableLog = false;

    #endregion

    #region File Ops

    [ObservableProperty] 
    private bool enableCompactView = false;

    [ObservableProperty]
    private bool stopPollingOnSync = false;

    [ObservableProperty]
    private bool allowMultiOp = true;

    [ObservableProperty]
    private bool rescanOnPush = true;

    [ObservableProperty]
    private bool keepDateModified = true;

    #endregion

    #region about

    /// <summary>
    /// GET releases on GitHub repo on each launch
    /// </summary>
    [ObservableProperty]
    private bool checkForUpdates = true;

    #endregion

    #region graphics

    /// <summary>
    /// Disables HW acceleration
    /// </summary>
    [ObservableProperty]
    private bool swRender = false;

    [JsonIgnore]
    [ObservableProperty]
    private bool isAnimated = true;

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
    private bool enableSplash = true;

    #endregion

    [ObservableProperty]
    private AppTheme theme = AppTheme.WindowsDefault;

    [ObservableProperty]
    private bool hidePasteNamingInfo = false;

    public string LastVersion { get; set; } = "0.0.0";

    [ObservableProperty]
    private bool showLaunchWsaMessage = true;

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
    private string uILanguage = "";

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
    private bool showLanguageNotification = true;

    [ObservableProperty]
    private bool homeLocationsExpanded = false;
}
