using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class FileActionsEnable : INotifyPropertyChanged
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

    private bool copyPathEnabled;
    public bool CopyPathEnabled
    {
        get => copyPathEnabled;
        set
        {
            if (Set(ref copyPathEnabled, value))
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

    private bool addToEditor;
    public bool EditFileEnabled
    {
        get => addToEditor;
        set => Set(ref addToEditor, value);
    }

    #endregion

    #region strings

    private string deleteAction;
    public string DeleteAction
    {
        get => deleteAction;
        set
        {
            if (Set(ref deleteAction, value))
                OnPropertyChanged(nameof(MenuDeleteTooltip));
        }
    }

    private string restoreAction;
    public string RestoreAction
    {
        get => restoreAction;
        set
        {
            if (Set(ref restoreAction, value))
                OnPropertyChanged(nameof(MenuRestoreTooltip));
        }
    }

    private string copyPathAction;
    public string CopyPathAction
    {
        get => copyPathAction;
        set => Set(ref copyPathAction, value);
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

    #region read only

    public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
    public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
    public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
    public bool PushPackageVisible => PushPackageEnabled && Data.Settings.EnableApk;
    public bool MoreEnabled => PackageActionsEnabled || CopyPathEnabled || UpdateModifiedEnabled;
    public string MenuDeleteTooltip => $"{DeleteAction} (Del)";
    public string MenuRestoreTooltip => $"{RestoreAction} (Ctrl+R)";
    public bool NameReadOnly => !RenameEnabled;
    public bool EmptyTrash => IsRecycleBin && !DeleteEnabled && !RestoreEnabled;
    public bool NewMenuVisible => !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive);
    public bool IsEditorTextChanged => OriginalEditorText != EditorText;

    #endregion

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
