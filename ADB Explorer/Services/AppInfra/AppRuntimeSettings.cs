using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public partial class AppRuntimeSettings : ViewModelBase
{
    public bool ResetAppSettings { get; set; } = false;

    private DriveViewModel browseDrive = null;
    public DriveViewModel BrowseDrive
    {
        get => browseDrive;
        set => Set(ref browseDrive, value);
    }

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

    private bool isExplorerLoaded = false;
    public bool IsExplorerLoaded
    {
        get => isExplorerLoaded;
        set => Set(ref isExplorerLoaded, value);
    }

    private bool isPollingStopped = false;
    public bool IsPollingStopped
    {
        get => isPollingStopped;
        set => Set(ref isPollingStopped, value);
    }

    private Cursor cursor = Cursors.Arrow;
    public Cursor MainCursor
    {
        get => cursor;
        set => Set(ref cursor, value);
    }

    private float mainWindowScalingFactor = 1.0f;
    public float MainWindowScalingFactor
    {
        get => mainWindowScalingFactor;
        set => Set(ref mainWindowScalingFactor, value);
    }

    private ThumbnailService.ThumbnailSize thumbsSize = ThumbnailService.ThumbnailSize.Disabled;
    public ThumbnailService.ThumbnailSize ThumbsSize
    {
        get => thumbsSize;
        set
        {
            thumbsSize = value;
            OnPropertyChanged();
        }
    }

    public string DefaultBrowserPath { get; set; }

    public string TempDragPath
    {
        get
        {
            field ??= Directory.CreateTempSubdirectory().FullName;

            return field;
        }
    } = null;

    public bool IsAppDeployed => Environment.CurrentDirectory.Equals(@"C:\WINDOWS\SYSTEM32", StringComparison.InvariantCultureIgnoreCase);

    public bool Is22H2 => Environment.OSVersion.Version >= AdbExplorerConst.WIN11_22H2;

    public bool IsRTL => Data.Settings.ActualUICulture.TextInfo.IsRightToLeft;

    #region Event-only properties

    public bool NewFolder { get => false; set => OnPropertyChanged(); }
    public bool NewFile { get => false; set => OnPropertyChanged(); }
    public bool Rename { get => false; set => OnPropertyChanged(); }
    public bool SelectAll { get => false; set => OnPropertyChanged(); }
    public bool Refresh { get => false; set => OnPropertyChanged(); }
    public bool FilterDrives { get => false; set => OnPropertyChanged(); }
    public bool FilterDevices { get => false; set => OnPropertyChanged(); }
    public bool FilterActions { get => false; set => OnPropertyChanged(); }
    public bool InitLister { get => false; set => OnPropertyChanged(); }    
    public bool DriveViewNav { get => false; set => OnPropertyChanged(); }
    public bool AutoHideSearchBox { get => false; set => OnPropertyChanged(); }

    #endregion
}
