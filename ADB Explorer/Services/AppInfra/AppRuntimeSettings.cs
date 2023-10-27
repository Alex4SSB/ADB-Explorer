using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;
using System.Collections;

namespace ADB_Explorer.Services;

public class AppRuntimeSettings : ViewModelBase
{
    public AppRuntimeSettings()
    {
        Data.Settings.PropertyChanged += (object sender, PropertyChangedEventArgs e) =>
        {
            if (e.PropertyName == nameof(Data.Settings.ForceFluentStyles))
                OnPropertyChanged(nameof(UseFluentStyles));
        };
    }

    public bool ResetAppSettings { get; set; } = false;

    private bool isSettingsPaneOpen = false;
    public bool IsSettingsPaneOpen
    {
        get => isSettingsPaneOpen;
        set
        {
            if (Set(ref isSettingsPaneOpen, value))
                DeviceHelper.CollapseDevices();
        }
    }

    private bool isDevicesPaneOpen = false;
    public bool IsDevicesPaneOpen
    {
        get => isDevicesPaneOpen;
        set
        {
            if (Set(ref isDevicesPaneOpen, value))
                DeviceHelper.CollapseDevices();
        }
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
        set
        {
            if (Set(ref isOperationsViewOpen, value))
                DeviceHelper.CollapseDevices();
        }
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

    private double maxSearchBoxWidth = AdbExplorerConst.DEFAULT_SEARCH_WIDTH;
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
        set
        {
            if (Set(ref deviceToBrowse, value))
            {
                if (value is not null)
                    DeviceHelper.OpenDevice(value);
            }
        }
    }

    private bool isManualPairingInProgress = false;
    public bool IsManualPairingInProgress
    {
        get => isManualPairingInProgress;
        set => Set(ref isManualPairingInProgress, value);
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
        set
        {
            if (Set(ref connectNewDevice, value))
            {
                if (value is not null)
                    DeviceHelper.ConnectDevice(value);
            }
        }
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

    private object locationToNavigate = NavHistory.SpecialLocation.None;
    public object LocationToNavigate
    {
        get => locationToNavigate;
        set
        {
            if (!Set(ref locationToNavigate, value))
                OnPropertyChanged();
        }
    }

    private string pathBoxNavigation = "";
    public string PathBoxNavigation
    {
        get => pathBoxNavigation;
        set
        {
            if (!Set(ref pathBoxNavigation, value))
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

    private IEnumerable explorerSource;
    public IEnumerable ExplorerSource
    {
        get => explorerSource;
        set => Set(ref explorerSource, value);
    }

    private LogicalDeviceViewModel currentDevice = null;
    public LogicalDeviceViewModel CurrentDevice
    {
        get => currentDevice;
        set => Set(ref currentDevice, value);
    }

    private bool? isPathBoxFocused = null;
    public bool? IsPathBoxFocused
    {
        get => isPathBoxFocused;
        set
        {
            if (!Set(ref isPathBoxFocused, value))
                OnPropertyChanged();
        }
    }

    private bool isSearchBoxFocused = false;
    public bool IsSearchBoxFocused
    {
        get => isSearchBoxFocused;
        set
        {
            if (!Set(ref isSearchBoxFocused, value))
                OnPropertyChanged();
        }
    }

    private bool isLogOpen = false;
    public bool IsLogOpen
    {
        get => isLogOpen;
        set => Set(ref isLogOpen, value);
    }

    private bool isLogPaused = false;
    public bool IsLogPaused
    {
        get => isLogPaused;
        set => Set(ref isLogPaused, value);
    }

    private string appTitle = $"{Properties.Resources.AppDisplayName}{Strings.S_NO_DEVICES_TITLE}";
    public string AppTitle
    {
        get => appTitle;
        set => Set(ref appTitle, value);
    }

    private bool isRootActive = false;
    public bool IsRootActive
    {
        get => isRootActive;
        set => Set(ref isRootActive, value);
    }

    public bool IsDebug
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    private bool isWindowLoaded = false;
    public bool IsWindowLoaded
    {
        get => isWindowLoaded;
        set => Set(ref isWindowLoaded, value);
    }

    private bool isExplorerLoaded = false;
    public bool IsExplorerLoaded
    {
        get => isExplorerLoaded;
        set => Set(ref isExplorerLoaded, value);
    }

    private bool? isPastViewVisible = null;
    public bool? IsPastViewVisible
    {
        get => isPastViewVisible;
        set => Set(ref isPastViewVisible, value);
    }

    public bool IsAppDeployed => Environment.CurrentDirectory.ToUpper() == @"C:\WINDOWS\SYSTEM32";

    public bool IsWin11 =>
#if DEBUG
        false;
#else
        Environment.OSVersion.Version >= AdbExplorerConst.WIN11_VERSION;
#endif


    public bool Is22H2 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_22H2;

    public bool HideForceFluent => !IsWin11;

    public bool UseFluentStyles => IsWin11 || Data.Settings.ForceFluentStyles;

    #region Event-only properties

    public bool NewFolder { get => false; set => OnPropertyChanged(); }
    public bool NewFile { get => false; set => OnPropertyChanged(); }
    public bool Rename { get => false; set => OnPropertyChanged(); }
    public bool SelectAll { get => false; set => OnPropertyChanged(); }
    public bool Refresh { get => false; set => OnPropertyChanged(); }
    public bool FilterDrives { get => false; set => OnPropertyChanged(); }
    public bool FilterDevices { get => false; set => OnPropertyChanged(); }
    public bool FilterActions { get => false; set => OnPropertyChanged(); }
    public bool ClearNavBox { get => false; set => OnPropertyChanged(); }
    public bool InitLister { get => false; set => OnPropertyChanged(); }
    public bool DriveViewNav { get => false; set => OnPropertyChanged(); }
    public bool AutoHideSearchBox { get => false; set => OnPropertyChanged(); }
    public bool RefreshFileOpControls { get => false; set => OnPropertyChanged(); }
    public bool ClearLogs { get => false; set => OnPropertyChanged(); }
    public bool RefreshSettingsControls { get => false; set => OnPropertyChanged(); }

    #endregion
}
