using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class AppSettings : INotifyPropertyChanged
{
    #region paths

    private string defaultFolder;
    /// <summary>
    /// Default Folder For Pull On Double-Click And Folder / File Browsers
    /// </summary>
    public string DefaultFolder
    {
        get => Get(ref defaultFolder, "");
        set
        {
            if (Set(ref defaultFolder, value))
                OnPropertyChanged(nameof(IsPullOnDoubleClickEnabled));
        }
    }

    public bool IsPullOnDoubleClickEnabled => !string.IsNullOrEmpty(DefaultFolder);

    private string manualAdbPath;
    public string ManualAdbPath
    {
        get => Get(ref manualAdbPath, "");
        set => Set(ref manualAdbPath, value);
    }

    #endregion

    #region File Behavior

    private bool showExtensions;
    public bool ShowExtensions
    {
        get => Get(ref showExtensions, true);
        set => Set(ref showExtensions, value);
    }

    private bool showSystemPackages;
    public bool ShowSystemPackages
    {
        get => Get(ref showSystemPackages, true);
        set => Set(ref showSystemPackages, value);
    }

    private bool showHiddenItems;
    public bool ShowHiddenItems
    {
        get => Get(ref showHiddenItems, true);
        set => Set(ref showHiddenItems, value);
    }

    #endregion

    #region Double Click

    private DoubleClickAction doubleClick;
    public DoubleClickAction DoubleClick
    {
        get => Get(ref doubleClick, DoubleClickAction.pull);
        set => Set(ref doubleClick, value);
    }

    #endregion

    #region device

    private bool saveDevices;
    public bool SaveDevices
    {
        get => Get(ref saveDevices, true);
        set => Set(ref saveDevices, value);
    }

    private bool autoOpen;
    /// <summary>
    /// Automatically Open Device For Browsing
    /// </summary>
    public bool AutoOpen
    {
        get => Get(ref autoOpen, false);
        set => Set(ref autoOpen, value);
    }

    private bool autoRoot;
    /// <summary>
    /// Automatically Try To Open Devices Using Root Privileges
    /// </summary>
    public bool AutoRoot
    {
        get => Get(ref autoRoot, false);
        set => Set(ref autoRoot, value);
    }

    #endregion

    #region Drives & Features

    private bool pollDrives;
    public bool PollDrives
    {
        get => Get(ref pollDrives, true);
        set => Set(ref pollDrives, value);
    }

    private bool enableRecycle;
    /// <summary>
    /// Enable moving deleted items to a special folder, instead of permanently deleting them
    /// </summary>
    public bool EnableRecycle
    {
        get => Get(ref enableRecycle, false);
        set => Set(ref enableRecycle, value);
    }

    private bool enableApk;
    public bool EnableApk
    {
        get => Get(ref enableApk, false);
        set => Set(ref enableApk, value);
    }

    #endregion

    #region adb

    private bool enableMdns;
    /// <summary>
    /// Runs ADB server with mDNS enabled, polling for services is enabled in the connection expander
    /// </summary>
    public bool EnableMdns
    {
        get => Get(ref enableMdns, false);
        set => Set(ref enableMdns, value);
    }

    private bool pollDevices;
    /// <summary>
    /// <see langword="false"/> - disables polling for both ADB devices and mDNS services. Enables manual refresh button
    /// </summary>
    public bool PollDevices
    {
        get => Get(ref pollDevices, true);
        set => Set(ref pollDevices, value);
    }

    private bool pollBattery;
    /// <summary>
    /// Enables battery information for all devices (polling and displaying)
    /// </summary>
    public bool PollBattery
    {
        get => Get(ref pollBattery, true);
        set => Set(ref pollBattery, value);
    }

    private bool enableLog;
    /// <summary>
    /// <see langword="false"/> - disables logging of commands and command log button
    /// </summary>
    public bool EnableLog
    {
        get => Get(ref enableLog, false);
        set => Set(ref enableLog, value);
    }

    #endregion

    private bool showExtendedView;
    /// <summary>
    /// File Operations Detailed View
    /// </summary>
    public bool ShowExtendedView
    {
        get => true; // Get(ref showExtendedView, true);
        //set => Set(ref showExtendedView, value);
    }

    #region about

    private bool? checkForUpdates;
    /// <summary>
    /// GET releases on GitHub repo on each launch
    /// </summary>
    public bool? CheckForUpdates
    {
        get
        {
            if (IsAppDeployed)
                return null;

            return Get(ref checkForUpdates, true);
        }
        set => Set(ref checkForUpdates, value);
    }

    #endregion

    #region graphics

    private bool forceFluentStyles;
    /// <summary>
    /// Use Fluent [Windows 11] Styles In Windows 10
    /// </summary>
    public bool ForceFluentStyles
    {
        get => Get(ref forceFluentStyles, false);
        set
        {
            if (Set(ref forceFluentStyles, value))
                OnPropertyChanged(nameof(UseFluentStyles));
        }
    }

    public bool UseFluentStyles => IsWin11 || ForceFluentStyles;

    private bool swRender;
    /// <summary>
    /// Disables HW acceleration
    /// </summary>
    public bool SwRender
    {
        get => Get(ref swRender, false);
        set => Set(ref swRender, value);
    }

    private bool isAnimated = true;
    /// <summary>
    /// This setting is not updated at runtime as long as we are unable to combine it with different enter and exit storyboards. <br />
    /// The issue is the inability to separate the change of this setting from the change of the original triggers.
    /// </summary>
    public bool IsAnimated
    {
        get => isAnimated;
        set
        {
            isAnimated = value;
            OnPropertyChanged(nameof(isAnimated));
        }
    }

    private bool disableAnimation;
    /// <summary>
    /// Disables all visual animations
    /// </summary>
    public bool DisableAnimation
    {
        get
        {
            var value = Get(ref disableAnimation, false);

            if (!WindowLoaded)
                IsAnimated = !disableAnimation;

            return value;
        }
        set => Set(ref disableAnimation, value);
    }

    private bool enableSplash;
    public bool EnableSplash
    {
        get => Get(ref enableSplash, true);
        set => Set(ref enableSplash, value);
    }

    #endregion

    #region theme

    private AppTheme appTheme;
    public AppTheme Theme
    {
        get => Get(ref appTheme, AppTheme.windowsDefault);
        set => Set(ref appTheme, value);
    }

    #endregion theme

    public static bool IsAppDeployed => Environment.CurrentDirectory.ToUpper() == @"C:\WINDOWS\SYSTEM32";

    public static bool IsWin11 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_VERSION;

    public static bool Is22H2 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_22H2;

    public bool HideForceFluent => !IsWin11;

    public bool WindowLoaded { get; set; } = false;


    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        Storage.StoreValue(propertyName, value);

        OnPropertyChanged(propertyName);

        return true;
    }
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    protected virtual T Get<T>(ref T storage, T defaultValue, [CallerMemberName] string propertyName = null)
    {
        if (storage is null || storage.Equals(default(T)))
        {
            var value = storage switch
            {
                Enum => Storage.RetrieveEnum<T>(propertyName),
                bool => Storage.RetrieveBool(propertyName),
                string => Storage.RetrieveValue(propertyName),
                null when typeof(T) == typeof(string) => Storage.RetrieveValue(propertyName),
                null when typeof(T) == typeof(bool?) => Storage.RetrieveBool(propertyName),
                _ => throw new NotImplementedException(),
            };

            storage = (T)(value is null ? defaultValue : value);
        }

        return storage;
    }
}

public class AppRuntimeSettings : INotifyPropertyChanged
{
    public bool ResetAppSettings { get; set; } = false;

    private bool isSettingsPaneOpen = false;
    public bool IsSettingsPaneOpen
    {
        get => isSettingsPaneOpen;
        set => Set(ref isSettingsPaneOpen, value);
    }

    private bool isDevicesPaneOpen = false;
    public bool IsDevicesPaneOpen
    {
        get => isDevicesPaneOpen;
        set => Set(ref isDevicesPaneOpen, value);
    }

    private bool isMdnsExpanderOpen = false;
    public bool IsMdnsExpanderOpen
    {
        get => isMdnsExpanderOpen;
        set => Set(ref isMdnsExpanderOpen, value);
    }

    private bool isOperationsViewOpen = false;
    public bool IsOperationsViewOpen
    {
        get => isOperationsViewOpen;
        set => Set(ref isOperationsViewOpen, value);
    }

    private bool sortedView = false;
    public bool SortedView
    {
        get => sortedView;
        set => Set(ref sortedView, value);
    }

    private bool groupsExpanded = false;
    public bool GroupsExpanded
    {
        get => groupsExpanded;
        set => Set(ref groupsExpanded, value);
    }

    private string searchText = "";
    public string SearchText
    {
        get => searchText;
        set => Set(ref searchText, value);
    }

    private double maxSearchBoxWidth = AdbExplorerConst.DEFAULT_MAX_SEARCH_WIDTH;
    public double MaxSearchBoxWidth
    {
        get => maxSearchBoxWidth;
        set => Set(ref maxSearchBoxWidth, value);
    }

    private bool collapseDevices = false;
    public bool CollapseDevices
    {
        get => collapseDevices;
        set => Set(ref collapseDevices, value);
    }

    private bool collapseDrives = false;
    public bool CollapseDrives
    {
        get => collapseDrives;
        set => Set(ref collapseDrives, value);
    }

    private bool updateCurrentDevice = false;
    public bool UpdateCurrentDevice
    {
        get => updateCurrentDevice;
        set => Set(ref updateCurrentDevice, value);
    }

    private UIDevice removeDevice = null;
    public UIDevice DeviceToRemove
    {
        get => removeDevice;
        set => Set(ref removeDevice, value);
    }

    private ServiceDevice deviceToPair;
    public ServiceDevice DeviceToPair
    {
        get => deviceToPair;
        set => Set(ref deviceToPair, value);
    }

    private bool isManualPairingInProgress = false;
    public bool IsManualPairingInProgress
    {
        get => isManualPairingInProgress;
        set => Set(ref isManualPairingInProgress, value);
    }

    private bool rootAttemptForbidden = false;
    public bool RootAttemptForbidden
    {
        get => rootAttemptForbidden;
        set => Set(ref rootAttemptForbidden, value);
    }

    private DriveViewModel browseDrive = null;
    public DriveViewModel BrowseDrive
    {
        get => browseDrive;
        set => Set(ref browseDrive, value);
    }

    private UIDevice connectNewDevice = null;
    public UIDevice ConnectNewDevice
    {
        get => connectNewDevice;
        set => Set(ref connectNewDevice, value);
    }

    private DateTime lastServerResponse = DateTime.Now;
    public DateTime LastServerResponse
    {
        get => lastServerResponse;
        set
        {
            lastServerResponse = value;
            OnPropertyChanged(nameof(lastServerResponse));

            OnPropertyChanged(nameof(TimeFromLastResponse));
            OnPropertyChanged(nameof(ServerUnresponsive));
        }
    }

    public string TimeFromLastResponse => $"{DateTime.Now.Subtract(LastServerResponse).TotalSeconds:0}";

    public bool ServerUnresponsive => Data.Settings.PollDevices && DateTime.Now.Subtract(LastServerResponse) > AdbExplorerConst.SERVER_RESPONSE_TIMEOUT;


    public event PropertyChangedEventHandler PropertyChanged;

    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
