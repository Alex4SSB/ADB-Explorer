using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.Views;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    private bool bfNavigation;

    private int ClickCount = 0;
    private bool WasSelected;
    private bool WasEditing;
    private bool WasDragging;
    private Point MouseDownPoint;
    private TextBox? _renameTextBox;

    private ExplorerViewModel ViewModel { get; }

    /// <summary>
    /// Returns the currently active items view (either <see cref="IconView"/> or <see cref="ExplorerGrid"/>).
    /// </summary>
    private Selector ActiveView => ViewModel.IsIconView ? IconView : ExplorerGrid;

    /// <summary>
    /// Returns the selected items from the currently active view.
    /// </summary>
    private System.Collections.IList ActiveSelectedItems => ViewModel.IsIconView
        ? IconView.SelectedItems
        : ExplorerGrid.SelectedItems;

    private void ActiveUnselectAll()
    {
        try
        {
            if (ViewModel.IsIconView)
                IconView.UnselectAll();
            else
                ExplorerGrid.UnselectAll();
        }
        catch
        { }
    }

    private void ActiveSelectAll()
    {
        if (ViewModel.IsIconView)
            IconView.SelectAll();
        else
            ExplorerGrid.SelectAll();
    }

    private void ToggleSelectAll()
    {
        if (ActiveView.Items.Count == ActiveSelectedItems.Count && ActiveSelectedItems.Count > 0)
            ActiveUnselectAll();
        else
            ActiveSelectAll();
    }

    private void ActiveScrollIntoView(object item)
    {
        if (item is null)
            return;

        if (ViewModel.IsIconView)
            IconView.ScrollIntoView(item);
        else
        {
            ExplorerGrid.ScrollIntoView(item);

            if (ExplorerScrollViewer?.ComputedHorizontalScrollBarVisibility is Visibility.Visible)
                ExplorerScrollViewer.ScrollToLeftEnd();
        }
    }

    public ScrollViewer ExplorerScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
            return field;
        }
    } = null;

    public ScrollViewer IconScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(IconView);
            return field;
        }
    } = null;

    public ScrollViewer ActiveScrollViewer => ViewModel.IsIconView ? IconScrollViewer : ExplorerScrollViewer;

    private static Point NullPoint => new(-1, -1);
    private double? RowHeight { get; set; }
    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight") + ScrollContentPresenterMargin;
    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;
    private double DataGridContentWidth
        => StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;

    private bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive)
                return false;

            if (ActiveView.SelectedItem is not FileClass file)
                return false;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            return vm.IsInEditMode;
        }
        set
        {
            if (value && !FileActions.RenameEnabled)
                return;

            if (ActiveView.SelectedItem is not FileClass file)
                return;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            vm.IsInEditMode = value;
            FileActions.IsExplorerEditing = value;
        }
    }

    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };
    private bool _isSyncingSelection = false;

    public ExplorerPageHeader(ExplorerViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture = Settings.ActualFormatCulture;

        DataContext =
        ViewModel = viewModel;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        InitializeComponent();

        PreviewTextInput += ExplorerPageHeader_PreviewTextInput;

        NavigationBox.UnfocusTarget =
        SearchBox.UnfocusTarget = ActiveView;

        SelectionTimer.Tick += SelectionTimer_Tick;

        DriveList.SelectionChanged += DriveList_SelectionChanged;

        FileIconView.RenameStarted += IconView_RenameStarted;
        FileIconView.RenameEnded += (_, _) => ClearRename();

        ViewModel.RequestModeRefresh = () =>
        {
            DetailsPane.RequestModeRefresh?.Invoke();
            DetailsControl.RequestModeRefresh?.Invoke();
        };

        ItemToSelect.PropertyChanged += (s, e) =>
        {
            ActiveView.SelectedItem = ItemToSelect.Value;
            if (ItemToSelect is not null)
                ActiveScrollIntoView(ItemToSelect.Value);
        };
    }

    private void ExplorerPageHeader_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsFocused || SearchBox.IsKeyboardFocusWithin
            || DetailsPane.IsEditorFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ActiveSelectedItems.Count;
        var selectedIndex = ActiveView.SelectedIndex;
        IBrowserItem? nextItem = null;

        for (int i = 0; i < ActiveView.Items.Count; i++)
        {
            var item = (IBrowserItem)ActiveView.Items[i];
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

        if (selectedIndex == ActiveView.SelectedIndex && nextItem is not null)
            ItemToSelect.Value = nextItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || SearchBox.IsKeyboardFocusWithin
            || NavigationBox.IsKeyboardFocusWithin
            || FileActions.IsExplorerEditing)
            return;
        
        bool handle = false;

        if (e.Key is Key.A && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ToggleSelectAll();
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
            handle |= ExplorerGridKeyNavigation(e.Key);
        }
        else if (FileActions.IsDriveViewVisible)
        {
            handle |= DriveViewKeyNavigation(e.Key);
        }

        e.Handled = handle;
    }

    private bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            switch (key)
            {
                case Key.Left or Key.Up:
                    DriveList.SelectedIndex = DriveList.Items.Count - 1;
                    break;

                case Key.Right or Key.Down:
                    DriveList.SelectedIndex = 0;
                    break;

                default:
                    return false;
            }

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        switch (key)
        {
            case Key.Enter:
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
                return true;

            case Key.Escape:
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;

            default:
                return false;
        }
    }

    private bool ExplorerGridKeyNavigation(Key key)
    {
        if (ActiveView.Items.Count < 1 || DetailsPane.IsEditorFocused)
            return false;

        switch (key)
        {
            case Key.Escape:
                ActiveUnselectAll();
                break;

            case Key.Left or Key.Right when !ViewModel.IsIconView:
                return false;

            case Key.Down or Key.Up or Key.Left or Key.Right or Key.Home or Key.End:
                if (bfNavigation)
                {
                    ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                    bfNavigation = false;
                }

                if (ViewModel.IsIconView)
                {
                    var navKey = key;
                    if (RuntimeSettings.IsRTL && navKey is Key.Left or Key.Right)
                        navKey = navKey == Key.Left ? Key.Right : Key.Left;

                    var step = navKey is Key.Left or Key.Right ? 1 : IconView.ItemsPerRow;

                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        IconView.MultiSelect(navKey, step, ViewModel);
                    else
                        IconView.SingleSelect(navKey, step, ViewModel);
                }
                else
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        ExplorerGrid.MultiSelect(key, ViewModel);
                    else
                        ExplorerGrid.SingleSelect(key, ViewModel);
                }
                break;

            case Key.Enter:
                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode)
                    return false;

                if (ActiveSelectedItems.Count == 1
                    && ActiveView.SelectedItem is FileClass selected
                    && FileActionLogic.CanEnterSelection(selected))
                    DoubleClick(ActiveView.SelectedItem);
                break;

            case Key.Apps:
                ActiveView.ContextMenu.IsOpen = true;
                break;

            default:
                return false;
        }

        return true;
    }

    private void SelectionTimer_Tick(object sender, EventArgs e)
    {
        SelectionTimer.Stop();
        ApplySelectionEffects();
    }

    private void ApplySelectionEffects()
    {
        SelectedFiles = FileActions.IsAppDrive ? [] : (DirList?.FileList?.Where(f => f.IsSelected) ?? []);
        SelectedPackages = FileActions.IsAppDrive ? ExplorerGrid.Items.OfType<Package>().Where(p => p.IsSelected) : [];
        FileActions.SelectedItemsCount = FileActions.IsAppDrive ? SelectedPackages.Count() : SelectedFiles.Count();

        if (DetailsPane.IsOpen)
        {
            DetailsPane.SelectedFiles = FileActions.IsAppDrive ? SelectedPackages : SelectedFiles;
        }

        if (DevicesObject.Current is { SupportsLsV2: false })
        {
            foreach (var file in SelectedFiles.Where(f => f.IsRegularFile && f.ShellLsSize is null))
            {
                if (DetailsPane.IsOpen && !file.IsCreationTimeResolved)
                    continue;

                file.UpdateSizeFromShell(CancellationToken.None);
            }
        }

        ViewModel.NotifySelectedFilesTotalSize();

        FileActionLogic.UpdateFileActions();
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.SafeInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive.Path);
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
                        bfNavigation = true;
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
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoBack());
                            break;
                        case Navigation.SpecialLocation.Forward:
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoForward());
                            break;
                        case Navigation.SpecialLocation.Up:
                            bfNavigation = false;
                            NavigateToPath(ParentPath);
                            break;
                        default:
                            bfNavigation = false;
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
                    Task.Run(() =>
                    {
                        FilterExplorerContextMenu();
                        ExplorerContextMenu.UpdateSeparators();
                    });
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    if (FileActions.RenameEnabled)
                        IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    ToggleSelectAll();
                    break;

                case nameof(AppRuntimeSettings.ThumbsSize):
                    OnThumbsSizeChanged();
                    break;

                default:
                    break;
            }
        });
    }

    private void PathBoxFocus(bool isFocused)
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
        }
    }

    private void FilterFileActions() => App.SafeInvoke(() => MainToolBar.Items?.Refresh());

    private void FilterExplorerContextMenu() => App.SafeInvoke(() =>
    {
        var collectionView = CollectionViewSource.GetDefaultView(ActiveView.ContextMenu.ItemsSource);
        if (collectionView is null)
            return;

        Predicate<object> predicate = m =>
        {
            var menu = m as SubMenu;

            if (menu.Children is null)
                return menu.Action.Command.IsEnabled;
            else
                return menu.Action.Command.IsEnabled && menu.Children.Any(child => child.Action.Command.IsEnabled);
        };

        collectionView.Filter = predicate;
    });

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ActiveScrollIntoView(newItem);
        ActiveView.SelectedItem = newItem;

        IsInEditMode = true;
        if (!IsInEditMode) // in case the editing element was not acquired
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void InitLister()
    {
        DirList = new(App.AppDispatcher, DevicesObject.Current, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private bool TrySelectBackNavigationItem()
    {
        if (!bfNavigation)
            return false;

        var path = NavHistory.TakePendingSelectionPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (NavHistory.FindBackNavigationItem(path) is not { } prevItem)
            return false;

        ItemToSelect.Value = prevItem;
        return true;
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.CurrentLocation):
                if (DetailsPane.IsOpen && ActiveSelectedItems.Count == 0)
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

                        App.SafeInvoke(() => FileActions.ListingInProgress = DirList.InProgress);
                    });

                    if (DirList.InProgress)
                        return;

                    if (FileActions.IsRecycleBin)
                    {
                        TrashHelper.EnableRecycleButtons();
                    }

                    if (!DirList.InProgress
                        && DetailsPane.IsOpen
                        && ActiveSelectedItems.Count == 0)
                    {
                        DetailsPane.RefreshSelection();
                    }

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when !DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    if (DirList.FileList.Count > 0)
                    {
                        SortExplorer();

                        if (!TrySelectBackNavigationItem())
                        {
                            ActiveScrollIntoView(ActiveView.Items[0]);

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

        Task.Delay(EXPLORER_NAV_DELAY).ContinueWith(_ => App.SafeInvoke(() => RuntimeSettings.IsExplorerLoaded = true));

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath, FileClass? locationSource = null)
    {
        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();

        DirList?.Stop();

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
        ActiveUnselectAll();

        if (DetailsPane.IsOpen)
            DetailsPane.SelectedFiles = [];

        ActiveView.Focus();

        NavigationBox.Path = realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath;
        CurrentDrive = DriveHelper.GetCurrentDrive(devicePath);
        FileActions.IsRecycleBin = realPath == RECYCLE_PATH;
        FileActions.IsAppDrive = realPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        FileActions.IsArchive = isArchive;
        FileActions.IsTemp = realPath == TEMP_PATH;
        FileActions.ParentEnabled = realPath != FileHelper.GetParentPath(realPath)
            && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        CurrentPath = realPath;

        FileActionLogic.IsPasteEnabled();

        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current?.Type is not DeviceType.Recovery;
        FileActions.UninstallPackageEnabled = false;

        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;

        FileActions.CopyPathDescription.Value = FileActions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        if (Settings.ThumbSizePerLocation)
        {
            ThumbnailService.ThumbnailSize size = ThumbnailService.ThumbnailSize.Disabled;
            Settings.LocationThumbSize.TryGetValue(CurrentPath, out size);
            ViewModel.CurrentThumbsSize = size;
        }
        else
        {
            ViewModel.CurrentThumbsSize = FileActions.IsAppDrive
                ? ThumbnailService.ThumbnailSize.Disabled
                : RuntimeSettings.ThumbsSize;
        }

        SortExplorer();

        if (FileActions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync(DeviceCts.Token).ContinueWith(_ => DirList.Navigate(realPath));

            FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            FileActions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true, DeviceCts.Token);
                FileActionLogic.UpdateFileActions();
                return true;
            }

            DirList.Navigate(realPath, locationSource);

            FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        ViewModel.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();

        return true;
    }

    private void SortExplorer()
    {
        if (Settings.SortingPerLocation && Settings.LocationSorting.TryGetValue(CurrentPath, out var sort))
        {
            ViewModel.SetSort(sort);
        }
        else
        {
            ViewModel.SetSort(SortingSelector.SortingProperty.Name, ListSortDirection.Ascending);
        }
    }

    private static void InvalidateFileIcons()
    {
        if (DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
            file.InvalidateIconViewModelThumbnail();
    }

    private static void DisposeFileIcons()
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
            var path = string.IsNullOrEmpty(location.Path)
                ? location.StringFromLocation()
                : location.Path;

            if (!FileActions.IsExplorerVisible)
                InitNavigation(path);
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

        FileActionLogic.ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        CurrentPath =
        NavigationBox.Path = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        CurrentDrive = null;

        if (!bfNavigation)
        {
            DriveList.SelectedIndex = -1;
            RuntimeSettings.SelectedDrive = null;
        }

        if (DriveList.SelectedIndex > -1)
        {
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();

            if (DetailsPane.IsOpen)
                DetailsPane.SelectedFiles = DriveList.SelectedItem is DriveViewModel selectedDrive ? [selectedDrive] : [];
        }

        RuntimeSettings.SelectedDrive = DriveList.SelectedItem as DriveViewModel;
        FileActionLogic.UpdateFileActions();
    }

    private void DataGridCell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is DataGridCell && e.TargetRect == Rect.Empty)
        {
            e.Handled = true;
        }
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        WasDragging = false;

        var cell = sender as DataGridCell;
        WasEditing = cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode;

        if (WasEditing)
            return;

        var row = DataGridRow.GetRowContainingElement(cell);
        var current = row.GetIndex();

        WasSelected = row.IsSelected;

        if (e.ChangedButton is MouseButton.Right && !WasSelected)
        {
            ExplorerGrid.UnselectAll();
            row.IsSelected = true;
            e.Handled = true;
            return;
        }

        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image || row.IsSelected
                     ? CopyPasteService.DragState.Pending
                     : CopyPasteService.DragState.None;

        MouseDownPoint = e.GetPosition(ExplorerGrid);
        e.Handled = true;
        ClickCount = e.ClickCount;

        if (ClickCount > 1)
        {
            DoubleClick(cell.DataContext);
            ClickCount = -1;
            return;
        }

        PathBoxFocus(false);
        RaiseUnfocusSearchBox();

        if (!row.IsSelected
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ExplorerGrid.UnselectAll();
            ExplorerGrid.SelectedIndex = -1;
            row.IsSelected = true;
        }

        ViewModel.NextSelectedIndex = current;
        ViewModel.CurrentSelectedIndex = current;
        if (ExplorerGrid.SelectedItems.Count < 1)
            ViewModel.FirstSelectedIndex = current;
    }

    private void DoubleClick(object source)
    {
        if (FileActions.IsRecycleBin)
            return;

        if (source is not FileClass file)
        {
            if (source is Package apk && !FileActions.ListingInProgress)
                FileActionLogic.OpenApkLocation(apk);

            return;
        }

        if (file.Type is FileType.Folder)
        {
            if (!FileActions.ListingInProgress)
            {
                bfNavigation = false;
                NavigateToPath(file);
            }

            return;
        }
        else if (file.Type is not FileType.File)
            return;

        if (!FileActions.IsAppDrive
            && DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, FileActions.IsArchive))
        {
            if (!FileActions.ListingInProgress)
            {
                bfNavigation = false;
                NavigateToPath(file);
            }

            return;
        }

        if (Settings.DoubleClickToPull
            && Settings.IsPullOnDoubleClickEnabled
            && FileActions.PullEnabled)
        {
            FileActionLogic.PullFiles(Settings.DefaultFolder);
        }
    }

    private void DataGridCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || ClickCount < 0)
            return;

        if (SelectionRect.IsActive)
        {
            SelectionRect.Collapse();
            e.Handled = true;

            return;
        }

        e.Handled = CellMouseUp(sender, e);

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
    }

    private bool CellMouseUp(object sender, MouseButtonEventArgs e)
    {
        DataGridCell cell;
        DataGridRow row;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Active || WasDragging)
        {
            WasDragging = false;
            return true;
        }

        switch (sender)
        {
            case DataGridCell c:
                {
                    cell = c;
                    row = DataGridRow.GetRowContainingElement(cell);

                    if (cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode)
                        return false;
                    break;
                }
            case DataGridRow r:
                row = r;
                cell = null;
                break;
            default:
                return false;
        }

        var current = row.GetIndex();
        ViewModel.CurrentSelectedIndex = current;

        if (MultiRowSelect(row))
            return true;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = current;
        }

        if (!row.IsSelected || ExplorerGrid.SelectedItems?.Count != 1)
        {
            ExplorerGrid.UnselectAll();
            row.IsSelected = true;
            return true;
        }

        if (cell?.Column == NameColumn)
            MouseUpOnName(cell);

        return true;
    }

    private void MouseUpOnName(DataGridCell cell)
    {
        if (!DevicesObject.Current.HasRootShell
            && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder))
            return;

        if (!FileActions.RenameEnabled)
            return;

        var file = (FileClass)ExplorerGrid.SelectedItem;
        var path = file.FullPath;

        if (ExplorerGrid.SelectedItems.Count == 1 && WasSelected && !WasEditing)
        {
            Task.Run(() =>
            {
                var start = DateTime.Now;

                while (true)
                {
                    Task.Delay(100);

                    if (DateTime.Now - start > RENAME_CLICK_DELAY)
                        break;

                    var currentPath = App.AppDispatcher?.Invoke(() => ((FileClass)ExplorerGrid.SelectedItem)?.FullPath);
                    if (ClickCount > 1 || currentPath != path)
                        return;
                }

                App.SafeInvoke(() =>
                {
                    file.FolderViewModel.IsInEditMode = true;
                    FileActions.IsExplorerEditing = true;
                });
            });
        }
    }

    private bool MultiRowSelect(DataGridRow row)
    {
        var current = row.GetIndex();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ExplorerGrid.UnselectAll();

            var firstSelected = ViewModel.FirstSelectedIndex;
            int firstUnselected = firstSelected, lastUnselected = current + 1;
            if (current < firstSelected)
            {
                firstUnselected = current;
                lastUnselected = firstSelected + 1;
            }

            for (int i = firstUnselected; i < lastUnselected; i++)
            {
                ExplorerGrid.SelectedItems.Add(ExplorerGrid.Items[i]);
            }

            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            row.IsSelected = !row.IsSelected;
            return true;
        }

        return false;
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        WasDragging = false;
        var row = sender as DataGridRow;

        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image || row.IsSelected
            ? CopyPasteService.DragState.Pending
            : CopyPasteService.DragState.None;

        ViewModel.SetIndexSingle(row.GetIndex());
    }

    private void ItemContainer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !FileActions.IsAppDrive && SelectedFiles.Count() == 1 && !IsInEditMode)
        {
            ClickCount = -1;
            DoubleClick(ActiveView.SelectedItem);
        }
    }

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
        //if (RuntimeSettings.IsSettingsPaneOpen || RuntimeSettings.IsDevicesPaneOpen)
        //    return;

        var key = e.Key;
        switch (key)
        {
            case Key.Enter when IsInEditMode:
                return;
            case Key.Enter:
                {
                    if (ExplorerGrid.SelectedItems.Count == 1
                        && ExplorerGrid.SelectedItem is FileClass selected
                        && FileActionLogic.CanEnterSelection(selected))
                        DoubleClick(ExplorerGrid.SelectedItem);
                    break;
                }
            case Key.Back:
                NavHistory.NavigateBF(Navigation.SpecialLocation.Back);
                break;

            case Key.Delete when FileActions.DeleteEnabled:
                FileActionLogic.DeleteFiles();
                break;

            case Key.Up or Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ExplorerGrid.MultiSelect(key, ViewModel);
                break;

            case Key.Up or Key.Down:
                ExplorerGrid.SingleSelect(key, ViewModel);
                break;

            case Key.F2:
                if (FileActions.RenameEnabled)
                    AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        CopyPaste.AcceptDataObject(e, (FrameworkElement)sender);
        e.Handled = true;
    }

    private void Row_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = true;
    }

    private void Row_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
        var allowed = CopyPaste.GetAllowedDragEffects(e.Data, (FrameworkElement)sender);

        if (allowed.HasFlag(DragDropEffects.Move) && CopyPaste.IsSelf && !e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) && !e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Link;
        }
        else if (allowed.HasFlag(DragDropEffects.Copy)) // copy is the default and does not require Ctrl to be activated
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
            e.Effects = allowed;

        if ((!allowed.HasFlag(DragDropEffects.Copy) && e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey)))
        {
            e.Effects = DragDropEffects.None;
        }

        CopyPaste.DropEffect =
        CopyPaste.CurrentDropEffect = e.Effects;

        if (CopyPaste.CurrentFiles.Any())
        {
            CopyPaste.DragBitmap = CopyPaste.CurrentFiles.First().DragImage;
        }

        e.Handled = true;
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        ViewModel.IsMenuOpen = false;
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!ViewModel.IsIconView)
        {
            var point = Mouse.GetPosition(ExplorerGrid);
            if (point.Y < ColumnHeaderHeight || WasDragging)
            {
                ViewModel.IsMenuOpen = false;
                e.Handled = true;
                return;
            }
        }
        else if (WasDragging)
        {
            ViewModel.IsMenuOpen = false;
            e.Handled = true;
            return;
        }

        ViewModel.IsMenuOpen = true;
        FileActionLogic.UpdateFileActions();
    }

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        if (RowHeight is null && ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row)
            RowHeight = row.ActualHeight;

        WasDragging = false;
        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image
                     ? CopyPasteService.DragState.Pending
                     : CopyPasteService.DragState.None;

        var point = e.GetPosition(ExplorerGrid);
        MouseDownPoint = point;

        int selectionIndex = ExplorerGrid.SelectedIndex;

        var actualRowWidth = ExplorerGrid.Columns
            .Where(col => col.Visibility == Visibility.Visible)
            .Sum(item => item.ActualWidth);

        if (point.Y > (ExplorerGrid.Items.Count * RowHeight + ColumnHeaderHeight)
            || point.Y > (ExplorerGrid.ActualHeight - StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid)?.ActualHeight % RowHeight)
            || point.Y < ColumnHeaderHeight + ScrollContentPresenterMargin
            || point.X > actualRowWidth
            || point.X > DataGridContentWidth)
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if (Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ExplorerGrid.UnselectAll();
                ExplorerGrid.SelectedIndex =
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(SelectionRect);
        bool withinEditingCell = false;
        DataGridCell cell = ExplorerGrid.SelectedCells.Count > 0
                            ? CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1])
                            : null;

        if (IsInEditMode && cell is not null)
        {
            withinEditingCell = VisualTreeHelper.GetDescendantBounds(cell).Contains(e.GetPosition(cell));
        }

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || withinEditingCell
            || ViewModel.IsMenuOpen;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending && (MouseDownPoint - point).LengthSquared >= 25)
        {
            if (ExplorerGrid.SelectedItems.Count > 0
                && ExplorerGrid.SelectedItems[0] is FileClass or Package
                && !abortDrag)
            {
                InitiateDrag(cell);
            }
            else
                CopyPaste.DragStatus = CopyPasteService.DragState.None;
        }

        if (abortDrag || CopyPaste.DragStatus is not CopyPasteService.DragState.None || WasDragging)
        {
            SelectionRect.Collapse();
            return;
        }

        SelectionRect.Update(point, MouseDownPoint, ExplorerScrollViewer, ActiveView, ActiveSelectedItems, ViewModel);
    }

    private void InitiateDrag(DependencyObject dragSource)
    {
        CopyPaste.DragStatus = CopyPasteService.DragState.Active;
        WasDragging = true;

        IEnumerable<FileClass> selectedItems;
        VirtualFileDataObject vfdo;
        if (FileActions.IsAppDrive)
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(ActiveSelectedItems.Cast<Package>());
            selectedItems = VirtualFileDataObject.SelfFiles;
        }
        else
        {
            selectedItems = ActiveSelectedItems.Cast<FileClass>();
            // Archive extract is copy-only (no cut / symlink from inside an archive).
            var effects = FileActions.IsArchive
                ? DragDropEffects.Copy
                : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

            vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, effects);
            if (FileActions.IsArchive && vfdo is not null)
                vfdo.PreferredDropEffect = DragDropEffects.Copy;
        }

        if (vfdo is not null)
        {
            CopyPaste.UpdateSelfVFDO(true);
            CopyPaste.DragBitmap = selectedItems.First().DragImage;

            vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, dragSource, vfdo.PreferredDropEffect.Value);
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        CommitRenameIfDeselected();

        if (ActiveSelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ActiveUnselectAll();
            return;
        }

        if (!ViewModel.SelectionInProgress)
        {
            if (ActiveSelectedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                if (ViewModel.FirstSelectedIndex < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    ViewModel.FirstSelectedIndex = ActiveView.SelectedIndex;
                }
            }
            else if (ActiveSelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.Items.IndexOf(e.AddedItems[0]);
            }
        }

        SyncSelectionToOtherView(sender, e);

        bool isOngoingMultiSelection = SelectionRect.IsActive
                || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (!isOngoingMultiSelection && ActiveSelectedItems.Count <= 1)
        {
            SelectionTimer.Stop();
            ApplySelectionEffects();
        }
        else if (!SelectionTimer.IsEnabled)
        {
            SelectionTimer.Start();
        }
    }

    private void SyncSelectionToOtherView(object sender, SelectionChangedEventArgs e)
    {
        if (FileActions.IsAppDrive)
        {
            // Fix IsSelected on Package items whose containers were recycled by virtualization
            // so UnselectAll() could not propagate through the TwoWay binding.
            var selectedSet = new HashSet<object>(ExplorerGrid.SelectedItems.Cast<object>());
            foreach (var item in ExplorerGrid.Items)
            {
                if (item is Package pkg && pkg.IsSelected != selectedSet.Contains(item))
                    pkg.IsSelected = !pkg.IsSelected;
            }
            return;
        }

        _isSyncingSelection = true;
        try
        {
            var sourceItems = (sender == ExplorerGrid
                ? ExplorerGrid.SelectedItems
                : IconView.SelectedItems).Cast<object>().ToList();

            System.Collections.IList targetItems = sender == ExplorerGrid
                ? IconView.SelectedItems
                : ExplorerGrid.SelectedItems;

            var toRemove = targetItems.Cast<object>()
                .Where(item => !sourceItems.Contains(item))
                .ToList();
            foreach (var item in toRemove)
                targetItems.Remove(item);

            foreach (var item in sourceItems)
            {
                if (!targetItems.Contains(item))
                    targetItems.Add(item);
            }

            // Fix IsSelected on underlying data items for virtualized containers
            // that had no container when UnselectAll() was called, and were therefore
            // skipped by the TwoWay binding propagation.
            var sourceSet = new HashSet<object>(sourceItems);
            foreach (var item in ExplorerGrid.Items)
            {
                if (item is FilePath fp && fp.IsSelected != sourceSet.Contains(item))
                    fp.IsSelected = !fp.IsSelected;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (FileActions.IsAppDrive)
            return;

        var sortedColumn = e.Column switch
        {
            var c when c == DateColumn => SortingSelector.SortingProperty.Date,
            var c when c == TypeColumn => SortingSelector.SortingProperty.Type,
            var c when c == SizeColumn => SortingSelector.SortingProperty.Size,
            _ => SortingSelector.SortingProperty.Name
        };

        var currentDirection = sortedColumn == ViewModel.SortedColumn ? ViewModel.SortDirection : null;
        var direction = ListHelper.Invert(currentDirection);
        ViewModel.SetSort(sortedColumn, direction);

        e.Column.SortDirection = direction;
        e.Handled = true;
    }

    private void BeginRename(TextBox textBox) => _renameTextBox = textBox;

    private void ClearRename() => _renameTextBox = null;

    private void CommitRenameIfDeselected()
    {
        if (_renameTextBox?.DataContext is not FileClass file)
        {
            ClearRename();
            return;
        }

        var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
        if (!vm.IsInEditMode)
        {
            ClearRename();
            return;
        }

        if (ActiveSelectedItems.Count == 1 && ReferenceEquals(ActiveSelectedItems[0], file))
            return;

        FileViewModelBase.RenameCommit(_renameTextBox, ViewModel.IsIconView ? ExitIconEditMode : ExitFolderEditMode);
    }

    private void ExitFolderEditMode(FileClass file)
    {
        file.FolderViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void ExitIconEditMode(FileClass file)
    {
        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameKeyDown(textBox, e.Key, ExitFolderEditMode);
        if (e.Key is Key.Escape or Key.F2 or Key.Enter)
            e.Handled = true;
    }

    private void NameColumnEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        var textBox = sender as TextBox;

        if (textBox.DataContext is FileClass file)
        {
            FileViewModelBase.PrepareRenameTextBox(textBox);
            BeginRename(textBox);
            RenameTooltipControl.Show(textBox, file.FolderViewModel);
        }
    }

    private void IconView_RenameStarted(object sender, TextBox textBox)
    {
        BeginRename(textBox);
        if (textBox.DataContext is FileClass file)
            RenameTooltipControl.Show(textBox, file.IconViewModel, centerHorizontally: true);
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (textBox.DataContext is FileClass file && !file.FolderViewModel.IsInEditMode)
            return;

        FileViewModelBase.RenameCommit(textBox, ExitFolderEditMode);
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameTextChanged(textBox);
    }

    private void SelectionRect_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SelectionRect.Collapse();

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = ViewModel.NextSelectedIndex;
        }
    }

    private void SelectionRect_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel.IsIconView)
            IconView_MouseMove(sender, e);
        else
            ExplorerGrid_MouseMove(sender, e);
    }

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        MouseDownPoint = NullPoint;
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        MouseDownPoint = NullPoint;

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

    private void IconView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        WasDragging = false;
        MouseDownPoint = e.GetPosition(SelectionRect);

        // Walk up from the original source to determine if the click is on an item or empty space
        var source = e.OriginalSource as DependencyObject;
        var hitItem = source is not null
            ? ItemsControl.ContainerFromElement(IconView, source) as ListViewItem
            : null;

        CopyPaste.DragStatus = hitItem is not null && (e.OriginalSource is TextBlock or Image || hitItem.IsSelected)
                     ? CopyPasteService.DragState.Pending
                     : CopyPasteService.DragState.None;

        int selectionIndex = IconView.SelectedIndex;

        if (hitItem is null)
        {
            // Ignore clicks on scrollbars
            for (var dep = source; dep is not null and not ListView; dep = VisualTreeHelper.GetParent(dep))
            {
                if (dep is System.Windows.Controls.Primitives.ScrollBar)
                    return;
            }

            if (IconView.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if (Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                IconView.UnselectAll();
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void IconView_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(SelectionRect);

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || ViewModel.IsMenuOpen;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending && (MouseDownPoint - point).LengthSquared >= 25)
        {
            if (IconView.SelectedItems.Count > 0
                && IconView.SelectedItems[0] is FileClass or Package
                && !abortDrag)
            {
                var dragSource = IconView.ItemContainerGenerator.ContainerFromItem(IconView.SelectedItems[0]) as DependencyObject ?? IconView;
                InitiateDrag(dragSource);
            }
            else
                CopyPaste.DragStatus = CopyPasteService.DragState.None;
        }

        if (abortDrag || CopyPaste.DragStatus is not CopyPasteService.DragState.None || WasDragging)
        {
            SelectionRect.Collapse();
            return;
        }

        SelectionRect.Update(point, MouseDownPoint, IconView.ScrollViewer, ActiveView, ActiveSelectedItems, ViewModel);
    }

    private void OnThumbsSizeChanged()
    {
        var size = RuntimeSettings.ThumbsSize;
        if (size != ThumbnailService.ThumbnailSize.Disabled)
            InvalidateFileIcons();

        App.SafeBeginInvoke(() =>
        {
            if (ActiveSelectedItems.Count > 0)
                ActiveScrollIntoView(ActiveSelectedItems[0]);
            else
                ActiveScrollViewer?.ScrollToTop();
        }, DispatcherPriority.Loaded);
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);

    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RuntimeSettings.SelectedDrive = DriveList.SelectedItem as DriveViewModel;
        DetailsPane.SelectedFiles = RuntimeSettings.SelectedDrive is DriveViewModel selectedDrive ? [selectedDrive] : [];
        FileActionLogic.UpdateFileActions();
    }

    private void DriveList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var hitItem = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(DriveList, source)
            : null;

        if (hitItem is not null)
            return;

        foreach (var item in DriveList.Items)
        {
            (item as DriveViewModel)?.IsSelected = false;
        }

        DriveList.SelectedIndex = -1;
        RuntimeSettings.SelectedDrive = null;
        FileActionLogic.UpdateFileActions();
    }

    private void ExplorerHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DetailsPane.MaxWidth = e.NewSize.Width - 100;
    }
}
