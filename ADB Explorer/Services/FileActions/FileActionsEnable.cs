namespace ADB_Explorer.Services;

public partial class FileActionsEnable : ObservableObject
{
    #region booleans

    public bool PushFilesFoldersEnabled { get; set; } = false;

    private bool _pushPackageEnabled = false;
    public bool PushPackageEnabled
    {
        get => _pushPackageEnabled;
        set 
        {
            if (FieldHelper.TrySet(ref _pushPackageEnabled, value))
            {
            }
        }
    }

    public bool ContextPushPackagesEnabled { get; set; }

    private bool _isCurrentLocationReadOnly;
    public bool IsCurrentLocationReadOnly
    {
        get => _isCurrentLocationReadOnly;
        set => SetProperty(ref _isCurrentLocationReadOnly, value);
    }

    private bool _isSelectionFuseProtectedAndroidRoot;
    public bool IsSelectionFuseProtectedAndroidRoot
    {
        get => _isSelectionFuseProtectedAndroidRoot;
        set => SetProperty(ref _isSelectionFuseProtectedAndroidRoot, value);
    }

    public bool IsCopyItemPathEnabled { get; set; }

    public bool IsCopyAsImageEnabled { get; set; }

    /// <summary>Tree context target is a saved-location node (<see cref="NavigationTreeNode.IsSavedLocation"/>).</summary>
    public bool RemoveSavedLocationEnabled { get; set; }

    public bool PackageActionsEnabled { get; set; }

    public bool InstallPackageEnabled { get; set; }

    public bool UninstallPackageEnabled { get; set; }

    public bool BackupPackageEnabled { get; set; }

    public bool SubmenuUninstallEnabled { get; set; }

    public bool CutEnabled { get; set; }

    public bool CopyEnabled { get; set; }

    public bool PasteEnabled { get; set; }

    public bool IsKeyboardPasteEnabled { get; set; }

    public bool RenameEnabled { get; set; }

    public bool RestoreEnabled { get; set; }

    public bool DeleteEnabled { get; set; }

    public bool NewEnabled { get; set; }

    public bool ContextNewEnabled { get; set; }

    /// <summary>The context menu's Sort and View submenus apply to the listing itself, so only with nothing selected.</summary>
    public bool IsContextSortViewEnabled { get; set; }

    private bool _isRegularItem;
    public bool IsRegularItem
    {
        get => _isRegularItem;
        set => SetProperty(ref _isRegularItem, value);
    }

    public bool IsSingleFolder { get; set; }

    public bool IsOpenInNewTabEnabled { get; set; }

    public bool PullEnabled { get; set; }

    public bool ContextPushEnabled { get; set; }

    private bool _isRecycleBin;
    public bool IsRecycleBin
    {
        get => _isRecycleBin;
        set
        {
            if (SetProperty(ref _isRecycleBin, value))
            {
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive && !IsArchive);
                IsRestoreMenuVisible.Value = value;
                IsPullCopyVisible.Value = !value;
                IsPasteVisible.Value = !IsAppDrive && !value;
            }
        }
    }

    private bool _isAppDrive;
    public bool IsAppDrive
    {
        get => _isAppDrive;
        set
        {
            if (SetProperty(ref _isAppDrive, value))
            {
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive && !IsArchive);
                IsCutPasteDeleteVisible.Value = !value;
                IsPasteVisible.Value = !value && !IsRecycleBin;
                OnPropertyChanged(nameof(IsAppDriveThumbsLocked));
            }
        }
    }

    /// <summary>
    /// App drive locks the thumbs-size control unless the device has <c>unzip</c>
    /// (required to load package icons for icon view).
    /// </summary>
    public bool IsAppDriveThumbsLocked =>
        IsAppDrive
        && (Data.ActiveDevice is not { } device || !ShellCommands.UnzipExists(device.ID));

    /// <summary>
    /// Re-raise <see cref="IsAppDriveThumbsLocked"/> when the open device or its
    /// <c>unzip</c> probe changes without <see cref="IsAppDrive"/> flipping.
    /// </summary>
    public void NotifyAppDriveThumbsLocked() => OnPropertyChanged(nameof(IsAppDriveThumbsLocked));

    private bool _isArchive;
    public bool IsArchive
    {
        get => _isArchive;
        set
        {
            if (FieldHelper.TrySet(ref _isArchive, value))
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive && !IsArchive);
        }
    }

    public bool WasInAppDrive { get; set; } = false;

    private bool _isTemp;
    public bool IsTemp
    {
        get => _isTemp;
        set => SetProperty(ref _isTemp, value);
    }

    public bool IsSearchMode { get; set; } = false;

    private bool _isExplorerVisible = false;
    public bool IsExplorerVisible
    {
        get => _isExplorerVisible;
        set
        {
            if (SetProperty(ref _isExplorerVisible, value))
                IsNewMenuVisible.Value = !IsExplorerVisible || (!IsRecycleBin && !IsAppDrive && !IsArchive);
        }
    }

    private bool _isDriveViewVisible = false;
    public bool IsDriveViewVisible
    {
        get => _isDriveViewVisible;
        set => SetProperty(ref _isDriveViewVisible, value);
    }

    public bool ParentEnabled { get; set; }

    public bool RefreshPackages { get; set; } = false;

    private bool _listingInProgress = false;
    public bool ListingInProgress
    {
        get => _listingInProgress;
        set => SetProperty(ref _listingInProgress, value);
    }

    public bool UpdateModifiedEnabled { get; set; }

    private bool _homeEnabled;
    public bool HomeEnabled
    {
        get => _homeEnabled;
        set => SetProperty(ref _homeEnabled, value);
    }

    public bool IsRefreshEnabled { get; set; } = false;

    public bool IsCopyCurrentPathEnabled { get; set; } = false;

    #endregion

    private string _explorerFilter = "";
    public string ExplorerFilter
    {
        get => _explorerFilter;
        set => SetProperty(ref _explorerFilter, value);
    }

    private bool _isExplorerEditing = false;
    public bool IsExplorerEditing
    {
        get => _isExplorerEditing;
        set => SetProperty(ref _isExplorerEditing, value);
    }

    public bool IsFollowLinkEnabled { get; set; } = false;

    /// <summary>
    /// Search mode's "open item location" - navigates to the selected item's own parent folder
    /// (as opposed to <see cref="IsFollowLinkEnabled"/>, which follows a link to its target).
    /// </summary>
    public bool IsOpenItemLocationEnabled { get; set; } = false;

    public bool IsPasteLinkEnabled { get; set; } = false;

    public bool IsCopyLinkEnabled { get; set; } = false;

    public bool IsCopyContentsEnabled { get; set; }

    public bool IsExtractHereEnabled { get; set; }

    public ObservableProperty<bool> IsCompressToEnabled = new();

    public ObservableProperty<bool> IsContextNewFileVisible = new() { Value = true };

    public ObservableProperty<bool> IsContextNewArchiveVisible = new() { Value = true };

    public bool IsCompressToContextEnabled { get; set; }

    public bool IsApkWebSearchEnabled { get; set; } = false;

    public bool IsOpenApkLocationEnabled { get; set; } = false;

    private int _selectedFilesCount = 0;
    public int SelectedItemsCount
    {
        get => _selectedFilesCount;
        set => SetProperty(ref _selectedFilesCount, value);
    }

    private bool _isPastingIllegalNaming = false;
    public bool IsPastingIllegalNaming
    {
        get => _isPastingIllegalNaming;
        set => SetProperty(ref _isPastingIllegalNaming, value);
    }

    private bool _isSelectionIllegalOnWindows = false;
    public bool IsSelectionIllegalOnWindows
    {
        get => _isSelectionIllegalOnWindows;
        set => SetProperty(ref _isSelectionIllegalOnWindows, value);
    }

    private bool _isSelectionIllegalNaming = false;
    public bool IsSelectionIllegalNaming
    {
        get => _isSelectionIllegalNaming;
        set => SetProperty(ref _isSelectionIllegalNaming, value);
    }

    private bool _isSelectionIllegalOnWinRoot = false;
    public bool IsSelectionIllegalOnWinRoot
    {
        get => _isSelectionIllegalOnWinRoot;
        set => SetProperty(ref _isSelectionIllegalOnWinRoot, value);
    }

    private bool _isSelectionConflictingNames = false;
    public bool IsSelectionConflictingNames
    {
        get => _isSelectionConflictingNames;
        set => SetProperty(ref _isSelectionConflictingNames, value);
    }

    private bool _isPastingConflictingNames = false;
    public bool IsPastingConflictingNames
    {
        get => _isPastingConflictingNames;
        set => SetProperty(ref _isPastingConflictingNames, value);
    }

    public bool IsPastingInDescendant { get; set; } = false;

    #region Observable properties

    public ObservableProperty<bool> IsCutState = new();

    public ObservableProperty<bool> IsCopyState = new();

    public ObservableProperty<bool> IsNewMenuVisible = new() { Value = true };

    public ObservableProperty<bool> IsRestoreMenuVisible = new() { Value = false };

    public ObservableProperty<bool> IsUninstallVisible = new() { Value = false };

    public ObservableProperty<bool> IsCutPasteDeleteVisible = new() { Value = true };

    public ObservableProperty<bool> IsPullCopyVisible = new() { Value = true };

    public ObservableProperty<bool> IsPasteVisible = new() { Value = true };

    public ObservableProperty<bool> IsApkActionsVisible = new();

    public ObservableProperty<bool> IsPushMenuVisible = new() { Value = true };

    public ObservableProperty<string> CopyPathDescription = new();

    public ObservableProperty<string> DeleteDescription = new();

    public ObservableProperty<string> ContextDeleteDescription = new();

    public ObservableProperty<string> RestoreDescription = new();

    public ObservableProperty<string> PasteDescription = new();

    public ObservableProperty<string> CutItemsCount = new();

    public ObservableProperty<string> PullDescription = new();

    public ObservableProperty<string> NavRefreshDescription = new() { Value = Strings.Resources.S_MENU_REFRESH };

    public ObservableProperty<BaseIcon> NavRefreshIcon = new() { Value = new BaseIcon("\uE72C", 16) };

    public ObservableProperty<BaseIcon> PasteIcon = new() { Value = new BaseIcon(() => new PasteIcon(), 18) };

    public ObservableProperty<BaseIcon> ContextPasteIcon = new() { Value = new BaseIcon(() => new PasteIcon(), 16) };

    #endregion

    #region read only

    public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
    public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
    public bool NameReadOnly => !RenameEnabled;
    public bool EmptyTrash => IsRecycleBin && !DeleteEnabled && !RestoreEnabled;
    /// <summary>The preview pane serves a regular file listing only.</summary>
    public bool IsPreviewAllowed => !IsRecycleBin && !IsAppDrive && IsExplorerVisible;
    public bool IsPasteStateVisible => IsExplorerVisible && Data.CopyPaste.PasteSource is not CopyPasteService.DataSource.None;

    #endregion
}
