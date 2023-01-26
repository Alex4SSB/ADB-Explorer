using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class AppRuntimeSettings : ViewModelBase
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

    private LogicalDeviceViewModel deviceToBrowse = null;
    public LogicalDeviceViewModel DeviceToOpen
    {
        get => deviceToBrowse;
        set => Set(ref deviceToBrowse, value);
    }

    private DeviceViewModel removeDevice = null;
    public DeviceViewModel DeviceToRemove
    {
        get => removeDevice;
        set => Set(ref removeDevice, value);
    }

    private ServiceDeviceViewModel deviceToPair;
    public ServiceDeviceViewModel DeviceToPair
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

    private DeviceViewModel connectNewDevice = null;
    public DeviceViewModel ConnectNewDevice
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

    private NavHistory.SpecialLocation locationToNavigate = NavHistory.SpecialLocation.None;
    public NavHistory.SpecialLocation LocationToNavigate
    {
        get => locationToNavigate;
        set
        {
            if (!Set(ref locationToNavigate, value))
                OnPropertyChanged();
        }
    }

    private bool isDevicesViewEnabled = false;
    public bool IsDevicesViewEnabled
    {
        get => isDevicesViewEnabled;
        set => Set(ref isDevicesViewEnabled, value);
    }

    private Version adbVersion;
    public Version AdbVersion
    {
        get => adbVersion;
        set => Set(ref adbVersion, value);
    }

    private int selectedDevicesCount = 0;
    public int SelectedDevicesCount
    {
        get => selectedDevicesCount;
        set => Set(ref selectedDevicesCount, value);
    }

    private bool isSplashScreenVisible = true;
    public bool IsSplashScreenVisible
    {
        get => isSplashScreenVisible;
        set => Set(ref isSplashScreenVisible, value);
    }

    #region Event-only properties

    public bool BeginPull { get => false; set => OnPropertyChanged(); }
    public bool PushFolders { get => false; set => OnPropertyChanged(); }
    public bool PushFiles { get => false; set => OnPropertyChanged(); }
    public bool PushPackages { get => false; set => OnPropertyChanged(); }
    public bool NewFolder { get => false; set => OnPropertyChanged(); }
    public bool NewFile { get => false; set => OnPropertyChanged(); }
    public bool Cut { get => false; set => OnPropertyChanged(); }
    public bool Copy { get => false; set => OnPropertyChanged(); }
    public bool Paste { get => false; set => OnPropertyChanged(); }
    public bool Rename { get => false; set => OnPropertyChanged(); }
    public bool Restore { get => false; set => OnPropertyChanged(); }
    public bool Delete { get => false; set => OnPropertyChanged(); }
    public bool Uninstall { get => false; set => OnPropertyChanged(); }
    public bool CopyItemPath { get => false; set => OnPropertyChanged(); }
    public bool UpdateModifiedTime { get => false; set => OnPropertyChanged(); }
    public bool EditItem { get => false; set => OnPropertyChanged(); }
    public bool InstallPackage { get => false; set => OnPropertyChanged(); }
    public bool CopyToTemp { get => false; set => OnPropertyChanged(); }
    public bool SelectAll { get => false; set => OnPropertyChanged(); }
    public bool Refresh { get => false; set => OnPropertyChanged(); }
    public bool EditCurrentPath { get => false; set => OnPropertyChanged(); }
    public bool Filter { get => false; set => OnPropertyChanged(); }

    #endregion
}
