using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    internal bool BfNavigation { get; set; }

    /// <summary>Open toolbar submenu depth (Main / Navigation / sorting / etc.).</summary>
    private int _toolbarSubmenuDepth;

    /// <summary>
    /// Coalesces <see cref="ExplorerHeader_SizeChanged"/>'s DetailsPane.MaxWidth recalculation -
    /// see that handler's own comment for why this is deferred rather than applied inline.
    /// </summary>
    private int _detailsPaneMaxWidthGeneration;

    internal int ToolbarSubmenuDepth => _toolbarSubmenuDepth;

    /// <summary>
    /// True after a toolbar submenu closed while the left button was still down —
    /// the dismiss click should not start rubber-band selection (it may still unselect).
    /// </summary>
    internal bool SuppressSelectionAfterMenu { get; set; }

    private ExplorerViewModel ViewModel { get; }

    internal DetailsPane DetailsPaneControl => DetailsPane;

    private void HookToolbarMenu(AdbMenu? menu)
    {
        if (menu is null)
            return;

        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnToolbarSubmenuOpened), true);
        menu.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(OnToolbarSubmenuClosed), true);
    }

    private void OnToolbarSubmenuOpened(object sender, RoutedEventArgs e) => _toolbarSubmenuDepth++;

    private void OnToolbarSubmenuClosed(object sender, RoutedEventArgs e)
    {
        _toolbarSubmenuDepth = Math.Max(0, _toolbarSubmenuDepth - 1);
        if (_toolbarSubmenuDepth != 0)
            return;

        ExplorerList.CancelExplorerMarquee();

        // Outside click dismisses with the button still down; Escape does not.
        if (Mouse.LeftButton is MouseButtonState.Pressed)
            SuppressSelectionAfterMenu = true;
    }

    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>
    /// Guards against a stray selection right after navigating (e.g. the second click of a
    /// double-click on a drive tile landing on the newly shown grid at the same screen position).
    /// Restarted on every navigation so back-to-back navigations don't race a stale continuation.
    /// </summary>
    private readonly DispatcherTimer _explorerLoadedTimer = new() { Interval = EXPLORER_NAV_DELAY };

    public ExplorerPageHeader(ExplorerViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture = Settings.ActualFormatCulture;

        DataContext =
        ViewModel = viewModel;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        FileActions.PropertyChanged += (_, e) => App.SafeInvoke(() =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.ExplorerFilter)
                && !string.IsNullOrEmpty(FileActions.ExplorerFilter))
            {
                ExplorerList.ClearSelectionForSearch();
            }

            if (e.PropertyName is nameof(FileActionsEnable.IsAppDriveThumbsLocked))
                ApplyLocationThumbSize();
        });

        Data.RunExplorerSearch += (_, _) => App.SafeInvoke(() =>
        {
            if (Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        });
        Data.ExitSearchMode += (_, _) => App.SafeInvoke(() => ExitSearchMode());

        InitializeComponent();

        ExplorerList.Initialize(this);

        Loaded += (_, _) =>
        {
            DragAutoScroll.Register(ExplorerList.ExplorerScrollViewer);
            DragAutoScroll.Register(ExplorerList.IconScrollViewer);
        };
        Unloaded += (_, _) =>
        {
            DragAutoScroll.Unregister(ExplorerList.ExplorerScrollViewer);
            DragAutoScroll.Unregister(ExplorerList.IconScrollViewer);
        };

        HookToolbarMenu(MainToolBar);
        HookToolbarMenu(NavigationToolBar);
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SortingSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(ThumbsSizeSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SearchOptionsControl));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(DetailsControl));

        PreviewTextInput += ExplorerPageHeader_PreviewTextInput;

        NavigationBox.UnfocusTarget =
        SearchBox.UnfocusTarget = ExplorerList.ActiveView;

        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RunExplorerSearch();
        };
        _explorerLoadedTimer.Tick += (_, _) =>
        {
            _explorerLoadedTimer.Stop();
            RuntimeSettings.IsExplorerLoaded = true;
        };

        ViewModel.RequestModeRefresh = () =>
        {
            DetailsPane.RequestModeRefresh?.Invoke();
            DetailsControl.RequestModeRefresh?.Invoke();
        };

        ItemToSelect.PropertyChanged += (s, e) =>
        {
            ExplorerList.ActiveView.SelectedItem = ItemToSelect.Value;
            if (ItemToSelect is not null)
                ExplorerList.ActiveScrollIntoView(ItemToSelect.Value);
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerViewModel.IsIconView)
                or nameof(ExplorerViewModel.IsContentView)
                or nameof(ExplorerViewModel.ExplorerItemsSource)
                or nameof(ExplorerViewModel.ExplorerSource))
            {
                ExplorerList.ScheduleApkIconPriorityUpdate();
            }

            // ActiveView switches to a different grid on view-mode change; a stale target
            // here means F6/Escape unfocus silently no-ops (Focus() on a hidden element fails).
            if (e.PropertyName is nameof(ExplorerViewModel.IsIconView) or nameof(ExplorerViewModel.IsContentView))
                NavigationBox.UnfocusTarget = SearchBox.UnfocusTarget = ExplorerList.ActiveView;
        };
    }

    private void ExplorerPageHeader_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsFocused || SearchBox.IsKeyboardFocusWithin
            || DetailsPane.IsEditorFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ExplorerList.ActiveSelectedItems.Count;
        var selectedIndex = ExplorerList.ActiveView.SelectedIndex;
        IBrowserItem? nextItem = null;

        for (int i = 0; i < ExplorerList.ActiveView.Items.Count; i++)
        {
            var item = (IBrowserItem)ExplorerList.ActiveView.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    ItemToSelect.Value = item;
                    break;
                }
                else
                    nextItem ??= item;
            }
        }

        if (selectedIndex == ExplorerList.ActiveView.SelectedIndex && nextItem is not null)
            ItemToSelect.Value = nextItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || SearchBox.IsKeyboardFocusWithin
            || NavigationBox.IsKeyboardFocusWithin
            || DetailsPane.IsEditorFocused
            || FileActions.IsExplorerEditing)
            return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && DevicesObject?.Current is not { IsOpen: true })
        {
            e.Handled = true;
            return;
        }

        bool handle = false;

        if (e.Key is Key.A && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ExplorerList.ToggleSelectAll();
            e.Handled = true;

            return;
        }
        
        if (e.Key is Key.Delete && FileActions.DeleteEnabled)
        {
            FileActionLogic.DeleteFiles();
            e.Handled = true;
            return;
        }

        if (!NAVIGATION_KEYS.Contains(e.Key))
            return;

        if (FileActions.IsExplorerVisible)
        {
            handle |= ExplorerList.ExplorerGridKeyNavigation(e.Key);
        }
        else if (FileActions.IsDriveViewVisible)
        {
            handle |= ExplorerList.DriveViewKeyNavigation(e.Key);
        }

        e.Handled = handle;
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.SafeInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive!.Path);
                    break;

                case nameof(AppRuntimeSettings.DriveViewNav):
                    if (!DiskUsagePollingService.ServerUnresponsive)
                        DriveViewNav();
                    break;

                case nameof(AppRuntimeSettings.InitLister):
                    InitLister();
                    break;

                case nameof(AppRuntimeSettings.PathBoxNavigation):
                    if (RuntimeSettings.PathBoxNavigation == "-")
                    {
                        BfNavigation = true;
                        NavigateToLocation(NavHistory.GoBack());
                    }
                    else
                    {
                        if (FileActions.IsExplorerVisible)
                            NavigateToLocation(new(RuntimeSettings.PathBoxNavigation));
                        else
                        {
                            if (!InitNavigation(RuntimeSettings.PathBoxNavigation))
                            {
                                DriveViewNav();
                            }
                        }
                    }
                    break;

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    if (RuntimeSettings.LocationToNavigate is null)
                        return;

                    DirList?.Stop();
                    DisposeFileIcons();

                    switch (RuntimeSettings.LocationToNavigate.Location)
                    {
                        case Navigation.SpecialLocation.Back:
                            BfNavigation = true;
                            NavigateToLocation(NavHistory.GoBack());
                            break;
                        case Navigation.SpecialLocation.Forward:
                            BfNavigation = true;
                            NavigateToLocation(NavHistory.GoForward());
                            break;
                        case Navigation.SpecialLocation.Up:
                            BfNavigation = false;
                            if (FileActions.IsSearchMode)
                                ExitSearchMode();
                            else
                                NavigateToPath(ParentPath);
                            break;
                        default:
                            BfNavigation = false;
                            if (FileActions.IsDriveViewVisible && RuntimeSettings.LocationToNavigate.Location is Navigation.SpecialLocation.DriveView)
                                        FileActionLogic.RefreshDrives(true, DeviceCts.Token);
                            else
                                NavigateToLocation(RuntimeSettings.LocationToNavigate);
                            break;
                    }
                    break;

                case nameof(AppRuntimeSettings.FilterActions):
                    Task.Run(() =>
                    {
                        if (FileActions.IsAppDrive || FileActions.IsRecycleBin || DevicesObject.Current is null)
                            FilterFileActions();
                    });
                    Task.Run(() => ExplorerContextMenu.UpdateSeparators());
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.CompressToExtension):
                    NewCompressItem();
                    break;

                case nameof(AppRuntimeSettings.PasteClipboardImage):
                    NewImagePasteItem();
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    if (FileActions.RenameEnabled)
                        ExplorerList.IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    ExplorerList.ToggleSelectAll();
                    break;

                case nameof(AppRuntimeSettings.ThumbsSize):
                    ExplorerList.OnThumbsSizeChanged();
                    break;

                case nameof(AppRuntimeSettings.IsSearchBoxFocused) when RuntimeSettings.IsSearchBoxFocused:
                    ExplorerList.ClearSelectionForSearch();
                    break;

                default:
                    break;
            }
        });
    }

    internal void PathBoxFocus(bool isFocused)
    {
        if (isFocused)
            _focusPathBox();
        else
            _unfocusPathBox();

        void _focusPathBox()
        {
            NavigationBox.Mode = NavigationBox.ViewMode.Path;
        }

        void _unfocusPathBox()
        {
            if (NavigationBox.Mode is NavigationBox.ViewMode.None)
                return;

            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

            if (NavigationBox.PathBox.IsKeyboardFocusWithin)
                NavigationBox.UnfocusTarget?.Focus();
        }
    }

    private void FilterFileActions() => App.SafeInvoke(() => MainToolBar.Items?.Refresh());

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode) // in case the editing element was not acquired
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewCompressItem()
    {
        var extension = RuntimeSettings.CompressToExtension;
        if (string.IsNullOrEmpty(extension) || !FileActionLogic.IsPendingCompress)
            return;

        var sources = FileActionLogic.GetPendingCompressSourcePaths();
        string baseName;
        if (sources.Count > 0)
        {
            var firstName = FileHelper.GetFullName(sources[0]);
            var firstExt = FileHelper.GetExtension(firstName);
            baseName = string.IsNullOrEmpty(firstExt) ? firstName : firstName[..^firstExt.Length];
        }
        else
            baseName = Strings.Resources.S_NEW_ARCHIVE;

        var fileName = FileHelper.DuplicateFile(DirList.FileList, $"{baseName}{extension}");
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingCompressTemp(newItem);

        DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveView.SelectedItem = newItem;
        ExplorerList.ActiveScrollViewer?.ScrollToTop();
        App.SafeBeginInvoke(() => ExplorerList.ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewImagePasteItem()
    {
        if (!FileActionLogic.IsPendingClipboardImage)
            return;

        var fileName = FileHelper.DuplicateFile(DirList.FileList, FileActionLogic.GetClipboardImageFileName());
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingClipboardImageTemp(newItem);

        DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        if (ViewModel.IsIconView && FileActionLogic.GetPendingClipboardImageThumbnail() is { } preview)
            newItem.IconViewModel.SetImmediatePreview(preview);

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void InitLister()
    {
        Files.Device = DevicesObject.Current;
        DirList = new(App.AppDispatcher, DevicesObject.Current, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private bool TrySelectBackNavigationItem()
    {
        if (!BfNavigation)
            return false;

        var path = NavHistory.TakePendingSelectionPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (NavHistory.FindBackNavigationItem(path) is not { } prevItem)
            return false;

        ItemToSelect.Value = prevItem;
        return true;
    }

    private void DirectoryLister_PropertyChanged(object? sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.CurrentLocation):
                // Empty selection shows CurrentLocation in the details pane. Refresh on location
                // changes only (preliminary + final). InProgress no longer re-triggers the same load.
                if (DetailsPane.IsOpen && ExplorerList.ActiveSelectedItems.Count == 0)
                    DetailsPane.RefreshSelection();
                break;

            case nameof(DirectoryLister.IsProgressVisible):
                UnfinishedBlock.Visible(DirList.IsProgressVisible);
                NavigationBox.IsLoadingProgressVisible = DirList.IsProgressVisible;
                break;

            case nameof(DirectoryLister.InProgress):
                {
                    Task.Run(() =>
                    {
                        if (!DirList.InProgress)
                            Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);

                        App.SafeInvoke(() =>
                        {
                            FileActions.ListingInProgress = DirList.InProgress;
                            FileActionLogic.UpdateFileActions();
                        });
                    });

                    if (DirList.InProgress)
                        return;

                    if (FileActions.IsRecycleBin)
                        TrashHelper.EnableRecycleButtons();

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when !DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    ViewModel.NotifyDirectoryLinksResolved();

                    if (DirList.FileList.Count > 0)
                    {
                        SortExplorer();

                        if (!TrySelectBackNavigationItem())
                        {
                            ExplorerList.ActiveScrollIntoView(ExplorerList.ActiveView.Items[0]);

                            if (Settings.ThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir
                                && !ThumbnailService.IsInitialized(DevicesObject.Current.SerialNumber)
                                && FileHelper.IsPhotoDir())
                            {
                                Task.Run(() => ThumbnailService.ForceLoad(DevicesObject.Current));
                            }
                        }
                    }

                    break;
                }
        }
    });

    private bool InitNavigation(string path = "")
    {
        if (path is null)
            return true;

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path);
        if (realPath is null)
            return false;

        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        RuntimeSettings.BrowseDrive = null;
        RuntimeSettings.SelectedDrive = null;

        // Re-arm the stray-selection guard for this navigation. Without resetting it here,
        // IsExplorerLoaded stays true after the very first navigation and never protects
        // later ones (e.g. the drive tile's second click landing on the newly shown grid).
        RuntimeSettings.IsExplorerLoaded = false;
        _explorerLoadedTimer.Stop();
        _explorerLoadedTimer.Start();

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath, FileClass? locationSource = null)
    {
        FileActions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Files.DirList?.Stop();

        ArchivePath.InvalidateCache();

        var deviceId = DevicesObject.Current?.ID;
        var isArchive = ArchivePath.IsArchivePath(realPath, deviceId);
        var devicePath = isArchive ? ArchivePath.GetArchivePath(realPath, deviceId) : realPath;

        FileActions.ListingInProgress = true;

        FileActions.WasInAppDrive = FileActions.IsAppDrive;
        FileActions.ExplorerFilter = "";
        NavHistory.Navigate(realPath);

        ViewModel.FirstSelectedIndex = -1;
        ViewModel.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPane.IsOpen)
            DetailsPane.SelectedFiles = [];

        ExplorerList.ActiveView.Focus();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath;
        CurrentDrive = DriveHelper.GetCurrentDrive(devicePath);
        FileActions.IsRecycleBin = realPath == RECYCLE_PATH;
        FileActions.IsAppDrive = realPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        FileActions.IsArchive = isArchive;
        FileActions.IsTemp = realPath == TEMP_PATH;
        FileActions.ParentEnabled = realPath != FileHelper.GetParentPath(realPath)
            && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        if (Files.DirList is null && DevicesObject.Current is not null)
            InitLister();

        if (FileActions.IsAppDrive && Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            Settings.SearchBox = SearchBox.SearchBoxMode.CurrentFolder;

        CurrentPath = realPath;

        FileActionLogic.IsPasteEnabled();

        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current is { Type: not DeviceType.Recovery };
        FileActions.UninstallPackageEnabled = false;

        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;
        FileActions.IsCutPasteDeleteVisible.Value = !FileActions.IsAppDrive;
        FileActions.IsPullCopyVisible.Value = !FileActions.IsRecycleBin;
        FileActions.IsPasteVisible.Value = !FileActions.IsAppDrive && !FileActions.IsRecycleBin;

        FileActions.CopyPathDescription.Value = FileActions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        ApplyLocationThumbSize();

        SortExplorer();

        if (FileActions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync(DeviceCts.Token).ContinueWith(_ => Files.DirList?.Navigate(realPath));

            FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            FileActions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true, DeviceCts.Token);
                FileActionLogic.UpdateFileActions();
                ExplorerList.ResetExplorerHorizontalScroll();
                return true;
            }

            if (Files.DirList is null)
                return false;

            Files.DirList.Navigate(realPath, locationSource);

            FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        if (Files.DirList is not null)
            ViewModel.ExplorerSource = Files.DirList.FileList;

        FileActionLogic.UpdateFileActions();

        ExplorerList.ResetExplorerHorizontalScroll();

        return true;
    }

    private void RunExplorerSearch()
    {
        if (Settings.SearchBox is not SearchBox.SearchBoxMode.AllSubfolders
            || !FileActions.IsExplorerVisible
            || FileActions.IsAppDrive
            || FileActions.IsRecycleBin
            || DevicesObject?.Current is null
            || DirList is null)
        {
            return;
        }

        var query = FileActions.ExplorerFilter?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (FileActions.IsSearchMode)
                ExitSearchMode();
            return;
        }

        if (!FileActions.IsSearchMode)
        {
            Data.SearchOriginPath = CurrentPath;
            var deviceId = DevicesObject?.Current?.ID;
            Data.SearchOriginCanWrite = DirList?.CurrentLocation is { FullPath: var locationPath, CanWriteLocation: true } location
                && locationPath == CurrentPath
                || deviceId is not null && DriveHelper.IsModificationAllowedAt(CurrentPath, deviceId);
        }

        var searchRoot = Data.SearchOriginPath;
        if (string.IsNullOrEmpty(searchRoot)
            || AdbLocation.LocationFromString(searchRoot) is not Navigation.SpecialLocation.None)
        {
            return;
        }

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        DirList.Stop();
        DisposeFileIcons();

        FileActions.ListingInProgress = true;
        FileActions.IsSearchMode = true;
        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        FileActions.IsRecycleBin = false;
        FileActions.IsAppDrive = false;
        FileActions.IsArchive = false;
        FileActions.IsTemp = false;
        FileActions.ParentEnabled = false;

        var searchPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.SearchMode);
        CurrentPath = searchPath;
        NavigationBox.Path = searchPath;
        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

        ViewModel.FirstSelectedIndex = -1;
        ViewModel.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPane.IsOpen)
            DetailsPane.SelectedFiles = [];

        ApplyLocationThumbSize();

        SortExplorer();
        DirList.Search(searchRoot, query, DeviceCts.Token);
        ViewModel.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();
        ExplorerList.ResetExplorerHorizontalScroll();

        if (DetailsPane.IsOpen)
            DetailsPane.RefreshSelection();
    }

    private void ExitSearchMode()
    {
        if (!FileActions.IsSearchMode)
            return;

        var origin = Data.SearchOriginPath;
        FileActions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;
        FileActions.ExplorerFilter = "";

        if (!string.IsNullOrEmpty(origin))
            NavigateToPath(origin);
    }

    private void SortExplorer()
    {
        if (Settings.SortingPerLocation && Settings.LocationSorting.TryGetValue(CurrentPath, out var sort))
        {
            if (FileActions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.Date or SortingSelector.SortingProperty.Size)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else if (!FileActions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.UserId or SortingSelector.SortingProperty.Version)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else
            {
                ViewModel.SetSort(sort);
            }
        }
        else
        {
            ViewModel.SetSort(SortingSelector.SortingProperty.Name, ListSortDirection.Ascending);
        }
    }

    private void ApplyLocationThumbSize()
    {
        if (FileActions.IsDriveViewVisible)
        {
            ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
            return;
        }

        if (FileActions.IsSearchMode)
        {
            ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Content;
            return;
        }

        if (string.IsNullOrEmpty(CurrentPath))
            return;

        if (FileActions.IsAppDriveThumbsLocked)
        {
            ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Disabled;
            return;
        }

        if (Settings.ThumbSizePerLocation)
        {
            ThumbnailService.ThumbnailSize size = ThumbnailService.ThumbnailSize.Disabled;
            Settings.LocationThumbSize.TryGetValue(CurrentPath, out size);
            ViewModel.CurrentThumbsSize = size;
        }
        else
        {
            ViewModel.CurrentThumbsSize = RuntimeSettings.ThumbsSize;
        }

        if (FileActions.IsAppDrive
            && Data.Packages is { Count: > 0 }
            && ApkIconService.IsEnabled)
            ApkIconService.BeginPreloadPackages(Data.Packages);
    }

    internal static void InvalidateFileIcons()
    {
        if (DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
            file.InvalidateIconViewModelThumbnail();
    }

    internal static void DisposeFileIcons()
    {
        if (DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
        {
            file.DisposeIconViewModel();
        }

        Task.Run(static () =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        });
    }

    private void NavigateToLocation(AdbLocation location)
    {
        ViewModel.IsMenuOpen = false;

        if (location.Location is Navigation.SpecialLocation.DriveView)
        {
            if (DiskUsagePollingService.ServerUnresponsive || DevicesObject?.Current is null)
                return;

            FileActions.IsRecycleBin = false;
            PathBoxFocus(false);
            RaiseUnfocusSearchBox();
            FileActionLogic.RefreshDrives(true, DeviceCts.Token);
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            if (location.Location is Navigation.SpecialLocation.SearchMode)
            {
                if (!string.IsNullOrEmpty(FileActions.ExplorerFilter))
                    RunExplorerSearch();
                return;
            }

            var path = string.IsNullOrEmpty(location.Path)
                ? location.StringFromLocation()
                : location.Path;

            if (!FileActions.IsExplorerVisible)
            {
                if (!InitNavigation(path))
                    DriveViewNav();
            }
            else
                NavigateToPath(path);
        }
    }

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!FileActions.IsAppDrive
            && DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, FileActions.IsArchive))
        {
            return _navigateToPath(ArchivePath.Join(file.FullPath, ""), file);
        }

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && _navigateToPath(realPath, file);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        var realPath = FolderHelper.FolderExists(path);
        if (realPath is null)
            return false;

        var locationSource = DirList?.FileList.FirstOrDefault(f => f.IsDirectory && f.FullPath == realPath);
        return _navigateToPath(realPath, locationSource);
    }

    private void DriveViewNav()
    {
        if (DiskUsagePollingService.ServerUnresponsive || DevicesObject?.Current is null)
            return;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        FileActionLogic.ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        CurrentPath =
        NavigationBox.Path = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        CurrentDrive = null;

        if (!BfNavigation)
        {
            ExplorerList.DriveListView.SelectedIndex = -1;
            RuntimeSettings.SelectedDrive = null;
        }

        if (ExplorerList.DriveListView.SelectedIndex > -1)
        {
            SelectionHelper.GetListViewItemContainer(ExplorerList.DriveListView).Focus();

            if (DetailsPane.IsOpen)
                DetailsPane.SelectedFiles = ExplorerList.DriveListView.SelectedItem is DriveViewModel selectedDrive ? [selectedDrive] : [];
        }

        RuntimeSettings.SelectedDrive = ExplorerList.DriveListView.SelectedItem as DriveViewModel;
        FileActionLogic.UpdateFileActions();

        ViewModel.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
    }

    public void ShowRenameTooltip(FrameworkElement anchor, object dataContext, bool centerHorizontally = false)
        => RenameTooltipControl.Show(anchor, dataContext, centerHorizontally);

    public void FocusActiveListing() => ExplorerList.ActiveView.Focus();

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        // Tunneling: run before UnselectAll so a nested MouseMove cannot start a rubber-band
        // from a stale down-point (item that was selected when the menu opened).
        if (_toolbarSubmenuDepth > 0 || SuppressSelectionAfterMenu)
        {
            SuppressSelectionAfterMenu = true;
            ExplorerList.CancelExplorerMarquee();
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        ExplorerList.EndExplorerMouseGesture();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
        => ExplorerList.ClearMouseDownPointIfIdle();

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left)
            ExplorerList.EndExplorerMouseGesture();

        if (FileActions.ListingInProgress && e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
            return;
        }

        e.Handled = e.ChangedButton switch
        {
            MouseButton.XButton1 => NavHistory.NavigateBF(Navigation.SpecialLocation.Back),
            MouseButton.XButton2 => NavHistory.NavigateBF(Navigation.SpecialLocation.Forward),
            _ => false,
        };
    }

    private void MainWin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CopyPaste.IsDrag && CopyPaste.DragStatus is not CopyPasteService.DragState.Active && e.Key is Key.Escape)
            CopyPaste.DragBitmap = null;
        else
            OnButtonKeyDown(sender, e);
    }

    public void HandlePreviewKeyDown(KeyEventArgs e) => MainWin_PreviewKeyDown(this, e);

    public void HandlePreviewKeyUp(KeyEventArgs e) => MainWindow_OnPreviewKeyUp(this, e);

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            CopyPaste.DragBitmap = null;
    }

    private void ExplorerHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        var generation = ++_detailsPaneMaxWidthGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _detailsPaneMaxWidthGeneration)
                return;

            DetailsPane.MaxWidth = ActualWidth - 100;
        }, DispatcherPriority.Loaded);
    }
}
