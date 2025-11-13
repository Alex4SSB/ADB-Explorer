using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal class FileActionsEnable : ViewModelBase
{
    public FileActionsEnable()
    {
        SelectedFileOps.PropertyChanged += (object sender, PropertyChangedEventArgs<IEnumerable<FileOperation>> e) => FileActionLogic.UpdateFileOpControls();
    }

    #region booleans

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

    private bool pushPackageEnabled = false;
    public bool PushPackageEnabled
    {
        get => pushPackageEnabled;
        set {
            if (Set(ref pushPackageEnabled, value))
            {
                OnPropertyChanged(nameof(PushEnabled));
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

    private bool isKeyboardPasteEnabled;
    public bool IsKeyboardPasteEnabled
    {
        get => isKeyboardPasteEnabled;
        set => Set(ref isKeyboardPasteEnabled, value);
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
            if (Set(ref isAppDrive, value))
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
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
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
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

    private bool isFileOpRingVisible = false;
    public bool IsFileOpRingVisible
    {
        get => isFileOpRingVisible;
        set => Set(ref isFileOpRingVisible, value);
    }

    #endregion

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

    private FileClass editorFilePath;
    public FileClass EditorAndroidPath
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

    private object itemToSelect = null;
    public object ItemToSelect
    {
        get => itemToSelect;
        set => Set(ref itemToSelect, value);
    }

    private bool isExplorerEditing = false;
    public bool IsExplorerEditing
    {
        get => isExplorerEditing;
        set => Set(ref isExplorerEditing, value);
    }

    private bool isFollowLinkEnabled = false;
    public bool IsFollowLinkEnabled
    {
        get => isFollowLinkEnabled;
        set => Set(ref isFollowLinkEnabled, value);
    }

    private bool isPasteLinkEnabled = false;
    public bool IsPasteLinkEnabled
    {
        get => isPasteLinkEnabled;
        set => Set(ref isPasteLinkEnabled, value);
    }

    private bool isApkWebSearchEnabled = false;
    public bool IsApkWebSearchEnabled
    {
        get => isApkWebSearchEnabled;
        set => Set(ref isApkWebSearchEnabled, value);
    }

    private bool isOpenApkLocationEnabled = false;
    public bool IsOpenApkLocationEnabled
    {
        get => isOpenApkLocationEnabled;
        set => Set(ref isOpenApkLocationEnabled, value);
    }

    private bool isRenameUnixLegal = false;
    public bool IsRenameUnixLegal
    {
        get => isRenameUnixLegal;
        set => Set(ref isRenameUnixLegal, value);
    }

    private bool isRenameFuseLegal = false;
    public bool IsRenameFuseLegal
    {
        get => isRenameFuseLegal;
        set => Set(ref isRenameFuseLegal, value);
    }

    private bool isRenameWindowsLegal = false;
    public bool IsRenameWindowsLegal
    {
        get => isRenameWindowsLegal;
        set => Set(ref isRenameWindowsLegal, value);
    }

    private bool isRenameDriveRootLegal = false;
    public bool IsRenameDriveRootLegal
    {
        get => isRenameDriveRootLegal;
        set => Set(ref isRenameDriveRootLegal, value);
    }

    private bool isRenameUnique = false;
    public bool IsRenameUnique
    {
        get => isRenameUnique;
        set => Set(ref isRenameUnique, value);
    }

    private int selectedFilesCount = 0;
    public int SelectedItemsCount
    {
        get => selectedFilesCount;
        set => Set(ref selectedFilesCount, value);
    }

    private bool isPastingIllegalOnFuse = false;
    public bool IsPastingIllegalOnFuse
    {
        get => isPastingIllegalOnFuse;
        set => Set(ref isPastingIllegalOnFuse, value);
    }

    private bool isSelectionIllegalOnWindows = false;
    public bool IsSelectionIllegalOnWindows
    {
        get => isSelectionIllegalOnWindows;
        set => Set(ref isSelectionIllegalOnWindows, value);
    }

    private bool isSelectionIllegalOnFuse = false;
    public bool IsSelectionIllegalOnFuse
    {
        get => isSelectionIllegalOnFuse;
        set => Set(ref isSelectionIllegalOnFuse, value);
    }

    private bool isSelectionIllegalOnWinRoot = false;
    public bool IsSelectionIllegalOnWinRoot
    {
        get => isSelectionIllegalOnWinRoot;
        set => Set(ref isSelectionIllegalOnWinRoot, value);
    }

    private bool isSelectionConflictingOnFuse = false;
    public bool IsSelectionConflictingOnFuse
    {
        get => isSelectionConflictingOnFuse;
        set => Set(ref isSelectionConflictingOnFuse, value);
    }

    private bool isPastingConflictingOnFuse = false;
    public bool IsPastingConflictingOnFuse
    {
        get => isPastingConflictingOnFuse;
        set => Set(ref isPastingConflictingOnFuse, value);
    }

    private bool isPastingInDescendant = false;
    public bool IsPastingInDescendant
    {
        get => isPastingInDescendant;
        set => Set(ref isPastingInDescendant, value);
    }

    public string messageToCopy = "";
    public string MessageToCopy
    {
        get => messageToCopy;
        set => Set(ref messageToCopy, value);
    }

    #region Observable properties

    public ObservableProperty<bool> IsCutState = new();

    public ObservableProperty<bool> IsCopyState = new();

    public ObservableProperty<bool> IsNewMenuVisible = new() { Value = true };

    public ObservableProperty<bool> IsRestoreMenuVisible = new() { Value = false };

    public ObservableProperty<bool> IsUninstallVisible = new() { Value = false };

    public ObservableProperty<bool> IsExpandSettingsVisible = new() { Value = true };

    public ObservableProperty<bool> IsLogToggleVisible = new() { Value = Data.Settings.EnableLog };

    public ObservableProperty<bool> IsApkActionsVisible = new() { Value = Data.Settings.EnableApk };


    public ObservableProperty<IEnumerable<FileOperation>> SelectedFileOps = new() { Value = [] };


    public ObservableProperty<string> CopyPathDescription = new();

    public ObservableProperty<string> DeleteDescription = new();

    public ObservableProperty<string> RestoreDescription = new();

    public ObservableProperty<string> PasteDescription = new();

    public ObservableProperty<string> CutItemsCount = new();

    public ObservableProperty<string> ValidateDescription = new();

    public ObservableProperty<string> RemoveFileOpDescription = new();

    public ObservableProperty<string> PullDescription = new();

    #endregion

    #region read only

    public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
    public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
    public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
    public bool MoreEnabled => EditFileEnabled || UpdateModifiedEnabled;
    public bool NameReadOnly => !RenameEnabled;
    public bool EmptyTrash => IsRecycleBin && !DeleteEnabled && !RestoreEnabled;
    public bool IsPasteStateVisible => IsExplorerVisible && !IsAppDrive && Data.CopyPaste.PasteSource is not CopyPasteService.DataSource.None;
    public bool IsEditorTextChanged => OriginalEditorText != EditorText;

    #endregion
}
