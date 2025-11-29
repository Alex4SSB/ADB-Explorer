using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using System.Collections;

namespace ADB_Explorer.Services;

public class AppRuntimeSettings : ViewModelBase
{
    public bool ResetAppSettings { get; set; } = false;

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
            {
                DeviceHelper.CollapseDevices();
                IsDetailedPeekMode = false;
            }
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

            try
            {
                App.Current?.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TimeFromLastResponse));
                    OnPropertyChanged(nameof(ServerUnresponsive));
                });
            }
            catch
            {
                if (App.Current?.Dispatcher is not null)
                    throw;
            }
        }
    }

    public string TimeFromLastResponse => $"{DateTime.Now.Subtract(LastServerResponse).TotalSeconds:0}";

    public bool ServerUnresponsive => Data.Settings.PollDevices && DateTime.Now.Subtract(LastServerResponse) > AdbExplorerConst.SERVER_RESPONSE_TIMEOUT;

    private AdbLocation locationToNavigate = new(Navigation.SpecialLocation.None);
    public AdbLocation LocationToNavigate
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

    private Version adbVersion;
    public Version AdbVersion
    {
        get => adbVersion;
        set
        {
            if (Set(ref adbVersion, value))
                OnPropertyChanged(nameof(AdbVersionString));
        }
    }

    public string AdbVersionString => $"\u200E - v{AdbVersion}";

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

    private bool isTerminalOpen = false;
    public bool IsTerminalOpen
    {
        get => isTerminalOpen;
        set => Set(ref isTerminalOpen, value);
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

    private string adbReadRate = null;
    public string AdbReadRate
    {
        get => adbReadRate;
        set => Set(ref adbReadRate, value);
    }

    private string adbWriteRate = null;
    public string AdbWriteRate
    {
        get => adbWriteRate;
        set => Set(ref adbWriteRate, value);
    }

    private string adbOtherRate = null;
    public string AdbOtherRate
    {
        get => adbOtherRate;
        set => Set(ref adbOtherRate, value);
    }

    private bool isAdbReadActive = false;
    public bool IsAdbReadActive
    {
        get => isAdbReadActive;
        set => Set(ref isAdbReadActive, value);
    }

    private bool isAdbWriteActive = false;
    public bool IsAdbWriteActive
    {
        get => isAdbWriteActive;
        set => Set(ref isAdbWriteActive, value);
    }

    private bool isPollingStopped = false;
    public bool IsPollingStopped
    {
        get => isPollingStopped;
        set => Set(ref isPollingStopped, value);
    }

    private bool isDetailedPeekMode = false;
    public bool IsDetailedPeekMode
    {
        get => isDetailedPeekMode;
        set => Set(ref isDetailedPeekMode, value);
    }

    private BitmapSource dragBitmap = null;
    public BitmapSource DragBitmap
    {
        get => dragBitmap;
        set => Set(ref dragBitmap, value);
    }

    private DragDropKeyStates dragModifiers = DragDropKeyStates.None;
    public DragDropKeyStates DragModifiers
    {
        get => dragModifiers;
        set => Set(ref dragModifiers, value);
    }

    private Cursor cursor = Cursors.Arrow;
    public Cursor MainCursor
    {
        get => cursor;
        set => Set(ref cursor, value);
    }

    private float dpiScalingFactor = 1.0f;
    public float DpiScalingFactor
    {
        get => dpiScalingFactor;
        set => Set(ref dpiScalingFactor, value);
    }

    private bool dragWithinSlave = false;
    public bool DragWithinSlave
    {
        get => dragWithinSlave;
        set => Set(ref dragWithinSlave, value);
    }

    private List<string> savedLocations = null;
    public List<string> SavedLocations
    {
        get
        {
            if (savedLocations is null)
            {
                var storage = Storage.RetrieveValue(nameof(SavedLocations));
                if (storage is not null && storage is string[] locations)
                {
                    savedLocations = [.. locations];
                }
            }
            return savedLocations;
        }

        set => Set(ref savedLocations, value);
    }

    public string DefaultBrowserPath { get; set; }

    public string AdbPath { get; set; }

    public string TempDragPath => FileHelper.ConcatPaths(Data.AppDataPath, AdbExplorerConst.TEMP_DRAG_FOLDER, '\\');

    public bool IsAppDeployed => Environment.CurrentDirectory.ToUpper() == @"C:\WINDOWS\SYSTEM32";

    public bool IsWin11 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_VERSION;

    public bool Is22H2 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_22H2;

    public bool HideForceFluent => false; // !IsWin11;

    public bool UseFluentStyles => true; // IsWin11 || Data.Settings.ForceFluentStyles;

    public bool IsRTL => Thread.CurrentThread.CurrentUICulture.TextInfo.IsRightToLeft;

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
    public bool SortFileOps { get => false; set => OnPropertyChanged(); }
    public bool RefreshExplorerSorting { get => false; set => OnPropertyChanged(); }
    public bool FinalizeSplash { get => false; set => OnPropertyChanged(); }
    public bool RefreshBreadcrumbs { get => false; set => OnPropertyChanged(); }

    #endregion
}
