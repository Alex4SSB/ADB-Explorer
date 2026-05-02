using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public partial class FileActionsEnable : ObservableObject
{
    #region booleans

    private bool pushFilesFoldersEnabled = false;
    public bool PushFilesFoldersEnabled
    {
        get => pushFilesFoldersEnabled;
        set
        {
            if (SetProperty(ref pushFilesFoldersEnabled, value))
                OnPropertyChanged(nameof(PushEnabled));
        }
    }

    private bool pushPackageEnabled = false;
    public bool PushPackageEnabled
    {
        get => pushPackageEnabled;
        set 
        {
            if (SetProperty(ref pushPackageEnabled, value))
            {
                OnPropertyChanged(nameof(PushEnabled));
            }
        }
    }

    private bool contextPushPackagesEnabled;
    public bool ContextPushPackagesEnabled
    {
        get => contextPushPackagesEnabled;
        set => SetProperty(ref contextPushPackagesEnabled, value);
    }

    [ObservableProperty]
    public partial bool IsCopyItemPathEnabled { get; set; }

    private bool packageActionsEnabled;
    public bool PackageActionsEnabled
    {
        get => packageActionsEnabled;
        set
        {
            if (SetProperty(ref packageActionsEnabled, value))
            {
                OnPropertyChanged(nameof(InstallUninstallEnabled));
                OnPropertyChanged(nameof(CopyToTempEnabled));
            }
        }
    }

    private bool installPackageEnabled;
    public bool InstallPackageEnabled
    {
        get => installPackageEnabled;
        set
        {
            if (SetProperty(ref installPackageEnabled, value))
            {
                OnPropertyChanged(nameof(InstallUninstallEnabled));
                OnPropertyChanged(nameof(CopyToTempEnabled));
            }
        }
    }

    private bool uninstallPackageEnabled;
    public bool UninstallPackageEnabled
    {
        get => uninstallPackageEnabled;
        set => SetProperty(ref uninstallPackageEnabled, value);
    }

    private bool submenuUninstallEnabled;
    public bool SubmenuUninstallEnabled
    {
        get => submenuUninstallEnabled;
        set => SetProperty(ref submenuUninstallEnabled, value);
    }

    private bool cutEnabled;
    public bool CutEnabled
    {
        get => cutEnabled;
        set => SetProperty(ref cutEnabled, value);
    }

    private bool copyEnabled;
    public bool CopyEnabled
    {
        get => copyEnabled;
        set => SetProperty(ref copyEnabled, value);
    }

    private bool pasteEnabled;
    public bool PasteEnabled
    {
        get => pasteEnabled;
        set => SetProperty(ref pasteEnabled, value);
    }

    private bool isKeyboardPasteEnabled;
    public bool IsKeyboardPasteEnabled
    {
        get => isKeyboardPasteEnabled;
        set => SetProperty(ref isKeyboardPasteEnabled, value);
    }

    private bool renameEnabled;
    public bool RenameEnabled
    {
        get => renameEnabled;
        set
        {
            if (SetProperty(ref renameEnabled, value))
                OnPropertyChanged(nameof(NameReadOnly));
        }
    }

    private bool restoreEnabled;
    public bool RestoreEnabled
    {
        get => restoreEnabled;
        set
        {
            if (SetProperty(ref restoreEnabled, value))
                OnPropertyChanged(nameof(EmptyTrash));
        }
    }

    private bool deleteEnabled;
    public bool DeleteEnabled
    {
        get => deleteEnabled;
        set
        {
            if (SetProperty(ref deleteEnabled, value))
                OnPropertyChanged(nameof(EmptyTrash));
        }
    }

    private bool newEnabled;
    public bool NewEnabled
    {
        get => newEnabled;
        set => SetProperty(ref newEnabled, value);
    }

    private bool contextNewEnabled;
    public bool ContextNewEnabled
    {
        get => contextNewEnabled;
        set => SetProperty(ref contextNewEnabled, value);
    }

    private bool isRegularItem;
    public bool IsRegularItem
    {
        get => isRegularItem;
        set => SetProperty(ref isRegularItem, value);
    }

    private bool pullEnabled;
    public bool PullEnabled
    {
        get => pullEnabled;
        set => SetProperty(ref pullEnabled, value);
    }

    private bool contextPushEnabled;
    public bool ContextPushEnabled
    {
        get => contextPushEnabled;
        set => SetProperty(ref contextPushEnabled, value);
    }

    private bool isRecycleBin;
    public bool IsRecycleBin
    {
        get => isRecycleBin;
        set
        {
            if (SetProperty(ref isRecycleBin, value))
            {
                OnPropertyChanged(nameof(EmptyTrash));
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
                IsRestoreMenuVisible.Value = value;
            }
        }
    }

    private bool isAppDrive;
    public bool IsAppDrive
    {
        get => isAppDrive;
        set
        {
            if (SetProperty(ref isAppDrive, value))
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
        }
    }

    private bool isTemp;
    public bool IsTemp
    {
        get => isTemp;
        set => SetProperty(ref isTemp, value);
    }

    private bool isExplorerVisible = false;
    public bool IsExplorerVisible
    {
        get => isExplorerVisible;
        set
        {
            if (SetProperty(ref isExplorerVisible, value))
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
        }
    }

    private bool isDriveViewVisible = false;
    public bool IsDriveViewVisible
    {
        get => isDriveViewVisible;
        set => SetProperty(ref isDriveViewVisible, value);
    }

    private bool parentEnabled;
    public bool ParentEnabled
    {
        get => parentEnabled;
        set => SetProperty(ref parentEnabled, value);
    }

    private bool refreshPackages = false;
    public bool RefreshPackages
    {
        get => refreshPackages;
        set => SetProperty(ref refreshPackages, value);
    }

    private bool listingInProgress = false;
    public bool ListingInProgress
    {
        get => listingInProgress;
        set => SetProperty(ref listingInProgress, value);
    }

    [ObservableProperty]
    public partial bool UpdateModifiedEnabled { get; set; }

    private bool homeEnabled;
    public bool HomeEnabled
    {
        get => homeEnabled;
        set => SetProperty(ref homeEnabled, value);
    }

    private bool isRefreshEnabled = false;
    public bool IsRefreshEnabled
    {
        get => isRefreshEnabled;
        set => SetProperty(ref isRefreshEnabled, value);
    }

    private bool isCopyCurrentPathEnabled = false;
    public bool IsCopyCurrentPathEnabled
    {
        get => isCopyCurrentPathEnabled;
        set => SetProperty(ref isCopyCurrentPathEnabled, value);
    }

    private bool isFileOpRingVisible = false;
    public bool IsFileOpRingVisible
    {
        get => isFileOpRingVisible;
        set => SetProperty(ref isFileOpRingVisible, value);
    }

    #endregion

    private string explorerFilter = "";
    public string ExplorerFilter
    {
        get => explorerFilter;
        set => SetProperty(ref explorerFilter, value);
    }

    private object itemToSelect = null;
    public object ItemToSelect
    {
        get => itemToSelect;
        set => SetProperty(ref itemToSelect, value);
    }

    private bool isExplorerEditing = false;
    public bool IsExplorerEditing
    {
        get => isExplorerEditing;
        set => SetProperty(ref isExplorerEditing, value);
    }

    private bool isFollowLinkEnabled = false;
    public bool IsFollowLinkEnabled
    {
        get => isFollowLinkEnabled;
        set => SetProperty(ref isFollowLinkEnabled, value);
    }

    private bool isPasteLinkEnabled = false;
    public bool IsPasteLinkEnabled
    {
        get => isPasteLinkEnabled;
        set => SetProperty(ref isPasteLinkEnabled, value);
    }

    private bool isApkWebSearchEnabled = false;
    public bool IsApkWebSearchEnabled
    {
        get => isApkWebSearchEnabled;
        set => SetProperty(ref isApkWebSearchEnabled, value);
    }

    private bool isOpenApkLocationEnabled = false;
    public bool IsOpenApkLocationEnabled
    {
        get => isOpenApkLocationEnabled;
        set => SetProperty(ref isOpenApkLocationEnabled, value);
    }

    private bool isRenameUnixLegal = false;
    public bool IsRenameUnixLegal
    {
        get => isRenameUnixLegal;
        set => SetProperty(ref isRenameUnixLegal, value);
    }

    private bool isRenameFuseLegal = false;
    public bool IsRenameFuseLegal
    {
        get => isRenameFuseLegal;
        set => SetProperty(ref isRenameFuseLegal, value);
    }

    private bool isRenameWindowsLegal = false;
    public bool IsRenameWindowsLegal
    {
        get => isRenameWindowsLegal;
        set => SetProperty(ref isRenameWindowsLegal, value);
    }

    private bool isRenameDriveRootLegal = false;
    public bool IsRenameDriveRootLegal
    {
        get => isRenameDriveRootLegal;
        set => SetProperty(ref isRenameDriveRootLegal, value);
    }

    private bool isRenameUnique = false;
    public bool IsRenameUnique
    {
        get => isRenameUnique;
        set => SetProperty(ref isRenameUnique, value);
    }

    private int selectedFilesCount = 0;
    public int SelectedItemsCount
    {
        get => selectedFilesCount;
        set => SetProperty(ref selectedFilesCount, value);
    }

    private bool isPastingIllegalOnFuse = false;
    public bool IsPastingIllegalOnFuse
    {
        get => isPastingIllegalOnFuse;
        set => SetProperty(ref isPastingIllegalOnFuse, value);
    }

    private bool isSelectionIllegalOnWindows = false;
    public bool IsSelectionIllegalOnWindows
    {
        get => isSelectionIllegalOnWindows;
        set => SetProperty(ref isSelectionIllegalOnWindows, value);
    }

    private bool isSelectionIllegalOnFuse = false;
    public bool IsSelectionIllegalOnFuse
    {
        get => isSelectionIllegalOnFuse;
        set => SetProperty(ref isSelectionIllegalOnFuse, value);
    }

    private bool isSelectionIllegalOnWinRoot = false;
    public bool IsSelectionIllegalOnWinRoot
    {
        get => isSelectionIllegalOnWinRoot;
        set => SetProperty(ref isSelectionIllegalOnWinRoot, value);
    }

    private bool isSelectionConflictingOnFuse = false;
    public bool IsSelectionConflictingOnFuse
    {
        get => isSelectionConflictingOnFuse;
        set => SetProperty(ref isSelectionConflictingOnFuse, value);
    }

    private bool isPastingConflictingOnFuse = false;
    public bool IsPastingConflictingOnFuse
    {
        get => isPastingConflictingOnFuse;
        set => SetProperty(ref isPastingConflictingOnFuse, value);
    }

    private bool isPastingInDescendant = false;
    public bool IsPastingInDescendant
    {
        get => isPastingInDescendant;
        set => SetProperty(ref isPastingInDescendant, value);
    }

    #region Observable properties

    public ObservableProperty<bool> IsCutState = new();

    public ObservableProperty<bool> IsCopyState = new();

    public ObservableProperty<bool> IsNewMenuVisible = new() { Value = true };

    public ObservableProperty<bool> IsRestoreMenuVisible = new() { Value = false };

    public ObservableProperty<bool> IsUninstallVisible = new() { Value = false };

    public ObservableProperty<bool> IsApkActionsVisible = new() { Value = Data.Settings.EnableApk };

    public ObservableProperty<string> CopyPathDescription = new();

    public ObservableProperty<string> DeleteDescription = new();

    public ObservableProperty<string> RestoreDescription = new();

    public ObservableProperty<string> PasteDescription = new();

    public ObservableProperty<string> CutItemsCount = new();

    public ObservableProperty<string> PullDescription = new();
    
    #endregion

    #region read only

    public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
    public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
    public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
    public bool NameReadOnly => !RenameEnabled;
    public bool EmptyTrash => IsRecycleBin && !DeleteEnabled && !RestoreEnabled;
    public bool IsPasteStateVisible => IsExplorerVisible && Data.CopyPaste.PasteSource is not CopyPasteService.DataSource.None;

    #endregion
}
