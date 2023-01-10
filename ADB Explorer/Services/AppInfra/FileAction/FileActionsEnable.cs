using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal class FileActionsEnable : ViewModelBase
{
    #region booleans

    private bool pushPullEnabled = true;
    public bool PushPullEnabled
    {
        get => pushPullEnabled;
        set => Set(ref pushPullEnabled, value);
    }

    private bool pushFilesFoldersEnabled = false;
    public bool PushFilesFoldersEnabled
    {
        get => pushFilesFoldersEnabled;
        set
        {
            if (Set(ref pushFilesFoldersEnabled, value))
                OnPropertyChanged(nameof(PushEnabled));
        }
    }

    private bool pushPackageEnabled;
    public bool PushPackageEnabled
    {
        get => pushPackageEnabled;
        set {
            if (Set(ref pushPackageEnabled, value))
            {
                OnPropertyChanged(nameof(PushEnabled));
                OnPropertyChanged(nameof(PushPackageVisible));
            }
        }
    }

    private bool contextPushPackagesEnabled;
    public bool ContextPushPackagesEnabled
    {
        get => contextPushPackagesEnabled;
        set => Set(ref contextPushPackagesEnabled, value);
    }

    private bool isCopyItemPathEnabled;
    public bool IsCopyItemPathEnabled
    {
        get => isCopyItemPathEnabled;
        set
        {
            if (Set(ref isCopyItemPathEnabled, value))
                OnPropertyChanged(nameof(MoreEnabled));
        }
    }

    private bool packageActionsEnabled;
    public bool PackageActionsEnabled
    {
        get => packageActionsEnabled;
        set
        {
            if (Set(ref packageActionsEnabled, value))
            {
                OnPropertyChanged(nameof(InstallUninstallEnabled));
                OnPropertyChanged(nameof(CopyToTempEnabled));
                OnPropertyChanged(nameof(MoreEnabled));
            }
        }
    }

    private bool installPackageEnabled;
    public bool InstallPackageEnabled
    {
        get => installPackageEnabled;
        set
        {
            if (Set(ref installPackageEnabled, value))
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
        set => Set(ref uninstallPackageEnabled, value);
    }

    private bool uninstallVisible;
    public bool UninstallVisible
    {
        get => uninstallVisible;
        set => Set(ref uninstallVisible, value);
    }

    private bool submenuUninstallEnabled;
    public bool SubmenuUninstallEnabled
    {
        get => submenuUninstallEnabled;
        set => Set(ref submenuUninstallEnabled, value);
    }

    private bool cutEnabled;
    public bool CutEnabled
    {
        get => cutEnabled;
        set => Set(ref cutEnabled, value);
    }

    private bool copyEnabled;
    public bool CopyEnabled
    {
        get => copyEnabled;
        set => Set(ref copyEnabled, value);
    }

    private bool pasteEnabled;
    public bool PasteEnabled
    {
        get => pasteEnabled;
        set => Set(ref pasteEnabled, value);
    }

    private bool renameEnabled;
    public bool RenameEnabled
    {
        get => renameEnabled;
        set
        {
            if (Set(ref renameEnabled, value))
                OnPropertyChanged(nameof(NameReadOnly));
        }
    }

    private bool restoreEnabled;
    public bool RestoreEnabled
    {
        get => restoreEnabled;
        set
        {
            if (Set(ref restoreEnabled, value))
                OnPropertyChanged(nameof(EmptyTrash));
        }
    }

    private bool deleteEnabled;
    public bool DeleteEnabled
    {
        get => deleteEnabled;
        set
        {
            if (Set(ref deleteEnabled, value))
                OnPropertyChanged(nameof(EmptyTrash));
        }
    }

    private bool newEnabled;
    public bool NewEnabled
    {
        get => newEnabled;
        set => Set(ref newEnabled, value);
    }

    private bool contextNewEnabled;
    public bool ContextNewEnabled
    {
        get => contextNewEnabled;
        set => Set(ref contextNewEnabled, value);
    }

    private bool isRegularItem;
    public bool IsRegularItem
    {
        get => isRegularItem;
        set => Set(ref isRegularItem, value);
    }

    private bool pullEnabled;
    public bool PullEnabled
    {
        get => pullEnabled;
        set => Set(ref pullEnabled, value);
    }

    private bool contextPushEnabled;
    public bool ContextPushEnabled
    {
        get => contextPushEnabled;
        set => Set(ref contextPushEnabled, value);
    }

    private bool isRecycleBin;
    public bool IsRecycleBin
    {
        get => isRecycleBin;
        set
        {
            if (Set(ref isRecycleBin, value))
            {
                OnPropertyChanged(nameof(EmptyTrash));
                OnPropertyChanged(nameof(NewMenuVisible));
            }
        }
    }

    private bool isAppDrive;
    public bool IsAppDrive
    {
        get => isAppDrive;
        set
        {
            if (Set(ref isAppDrive, value))
                OnPropertyChanged(nameof(NewMenuVisible));
        }
    }

    private bool isTemp;
    public bool IsTemp
    {
        get => isTemp;
        set => Set(ref isTemp, value);
    }

    private bool isExplorerVisible = false;
    public bool IsExplorerVisible
    {
        get => isExplorerVisible;
        set
        {
            if (Set(ref isExplorerVisible, value))
                OnPropertyChanged(nameof(NewMenuVisible));
        }
    }

    private bool isDriveViewVisible = false;
    public bool IsDriveViewVisible
    {
        get => isDriveViewVisible;
        set => Set(ref isDriveViewVisible, value);
    }

    private bool parentEnabled;
    public bool ParentEnabled
    {
        get => parentEnabled;
        set => Set(ref parentEnabled, value);
    }

    private bool refreshPackages = false;
    public bool RefreshPackages
    {
        get => refreshPackages;
        set => Set(ref refreshPackages, value);
    }

    private bool listingInProgress = false;
    public bool ListingInProgress
    {
        get => listingInProgress;
        set => Set(ref listingInProgress, value);
    }

    private bool updateModifiedEnabled;
    public bool UpdateModifiedEnabled
    {
        get => updateModifiedEnabled;
        set
        {
            if (Set(ref updateModifiedEnabled, value))
                OnPropertyChanged(nameof(MoreEnabled));
        }
    }

    private bool homeEnabled;
    public bool HomeEnabled
    {
        get => homeEnabled;
        set => Set(ref homeEnabled, value);
    }

    private bool editorEnabled;
    public bool IsEditorOpen
    {
        get => editorEnabled;
        set => Set(ref editorEnabled, value);
    }

    private bool editFileEnabled;
    public bool EditFileEnabled
    {
        get => editFileEnabled;
        set => Set(ref editFileEnabled, value);
    }

    private bool isRefreshEnabled = false;
    public bool IsRefreshEnabled
    {
        get => isRefreshEnabled;
        set => Set(ref isRefreshEnabled, value);
    }

    private bool isCopyCurrentPathEnabled = false;
    public bool IsCopyCurrentPathEnabled
    {
        get => isCopyCurrentPathEnabled;
        set => Set(ref isCopyCurrentPathEnabled, value);
    }

    private FileClass.CutType isCopyPasteAction = FileClass.CutType.None;
    public FileClass.CutType PasteState
    {
        get => isCopyPasteAction;
        set
        {
            if (Set(ref isCopyPasteAction, value))
            {
                IsCutState.Value = value is FileClass.CutType.Cut;
                IsCopyState.Value = value is FileClass.CutType.Copy;
            }
        }
    }

    private string originalEditorText;
    public string OriginalEditorText
    {
        get => originalEditorText;
        set
        {
            if (Set(ref originalEditorText, value))
                OnPropertyChanged(nameof(IsEditorTextChanged));
        }
    }

    private string editorText;
    public string EditorText
    {
        get => editorText;
        set
        {
            if (Set(ref editorText, value))
                OnPropertyChanged(nameof(IsEditorTextChanged));
        }
    }

    private string editorFilePath;
    public string EditorFilePath
    {
        get => editorFilePath;
        set => Set(ref editorFilePath, value);
    }

    private string explorerFilter = "";
    public string ExplorerFilter
    {
        get => explorerFilter;
        set => Set(ref explorerFilter, value);
    }

    #endregion

    #region Observable properties

    public ObservableProperty<string> CopyPathAction = new();

    public ObservableProperty<string> DeleteAction = new();

    public ObservableProperty<string> RestoreAction = new();

    public ObservableProperty<string> PasteAction = new();

    public ObservableProperty<string> CutItemsCount = new();
    
    public ObservableProperty<bool> IsCutState = new();

    public ObservableProperty<bool> IsCopyState = new();

    #endregion

    #region read only

    public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
    public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
    public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
    public bool PushPackageVisible => PushPackageEnabled && Data.Settings.EnableApk;
    public bool MoreEnabled => PackageActionsEnabled || IsCopyItemPathEnabled || UpdateModifiedEnabled;
    public bool NameReadOnly => !RenameEnabled;
    public bool EmptyTrash => IsRecycleBin && !DeleteEnabled && !RestoreEnabled;
    public bool NewMenuVisible => !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
    public bool IsPasteStateVisible => IsExplorerVisible && !IsRecycleBin && !IsAppDrive;
    public bool IsEditorTextChanged => OriginalEditorText != EditorText;

    #endregion
}
