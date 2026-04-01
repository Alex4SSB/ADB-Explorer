using ADB_Explorer.Converters;
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
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{

    private string prevPath = "";

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    private bool bfNavigation;

    private int ClickCount = 0;
    private bool WasSelected;
    private bool WasEditing;
    private bool WasDragging;
    private Point MouseDownPoint;

    private ExplorerViewModel ViewModel => (ExplorerViewModel)DataContext;

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
        if (ViewModel.IsIconView)
            IconView.UnselectAll();
        else
            ExplorerGrid.UnselectAll();
    }

    private void ActiveSelectAll()
    {
        if (ViewModel.IsIconView)
            IconView.SelectAll();
        else
            ExplorerGrid.SelectAll();
    }

    private void ActiveScrollIntoView(object item)
    {
        if (ViewModel.IsIconView)
            IconView.ScrollIntoView(item);
        else
            ExplorerGrid.ScrollIntoView(item);
    }

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
            if (ActiveView.SelectedItem is not FileClass file)
                return;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            vm.IsInEditMode = value;
            FileActions.IsExplorerEditing = value;
        }
    }

    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };

    public ExplorerPageHeader()
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Settings.UICulture;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        InitializeComponent();

        KeyDown += new KeyEventHandler(OnButtonKeyDown);
        PreviewTextInput += new TextCompositionEventHandler(MainWindow_PreviewTextInput);

        AppActions.Bindings.ForEach(binding =>
        {
            InputBindings.Add(binding);
            ExplorerGrid.InputBindings.Add(binding);
            IconView.InputBindings.Add(binding);
        });

        SelectionTimer.Tick += SelectionTimer_Tick;
    }

    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ActiveSelectedItems.Count;
        var selectedIndex = ActiveView.SelectedIndex;
        object altItem = null;

        for (int i = 0; i < ActiveView.Items.Count; i++)
        {
            var item = ActiveView.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    FileActions.ItemToSelect = item;
                    break;
                }
                else
                    altItem ??= item;
            }
        }

        if (selectedIndex == ActiveView.SelectedIndex && altItem is not null)
            FileActions.ItemToSelect = altItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || !NAVIGATION_KEYS.Contains(e.Key))
            return;

        bool handle = false;

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
        if (ActiveView.Items.Count < 1)
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

                if (ActiveSelectedItems.Count == 1 && ((FilePath)ActiveView.SelectedItem).IsDirectory)
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
        SelectedFiles = FileActions.IsAppDrive ? [] : ActiveSelectedItems.OfType<FileClass>();
        SelectedPackages = FileActions.IsAppDrive ? ActiveSelectedItems.OfType<Package>() : [];
        FileActions.SelectedItemsCount = FileActions.IsAppDrive ? SelectedPackages.Count() : SelectedFiles.Count();

        ViewModel.NotifySelectedFilesTotalSize();

        FileActionLogic.UpdateFileActions();
        //MainToolBar.Items?.Refresh();
        //PasteGrid.Visibility = Visibility.Visible;
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
                                FileActionLogic.RefreshDrives(true);
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
                    IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.IsPathBoxFocused):
                    IsPathBoxFocused(RuntimeSettings.IsPathBoxFocused
                                     ?? NavigationBox.Mode
                                     is NavigationBox.ViewMode.Breadcrumbs);

                    RuntimeSettings.AutoHideSearchBox = true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    if (ActiveView.Items.Count == ActiveSelectedItems.Count)
                        ActiveUnselectAll();
                    else
                        ActiveSelectAll();
                    break;

                default:
                    break;
            }
        });
    }

    private void IsPathBoxFocused(bool isFocused)
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

            NavigationBox.UnfocusTarget = ActiveView;
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
            FileActionLogic.CreateNewItem(newItem);
    }

    private void InitLister()
    {
        DirList = new(App.AppDispatcher, CurrentADBDevice, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
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

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when ActiveView.Items.Count < 1 || !DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished) when bfNavigation
                    && !string.IsNullOrEmpty(prevPath) && DirList.FileList.FirstOrDefault(item => item.FullPath == prevPath) is var prevItem and not null:
                FileActions.ItemToSelect = prevItem;

                break;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    if (ActiveView.Items.Count > 0)
                    {
                        ActiveScrollIntoView(ActiveView.Items[0]);

                        if (Settings.ThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir
                            && !ThumbnailService.IsInitialized(CurrentADBDevice.Device.LogicalID)
                            && FileHelper.IsPhotoDir())
                        {
                            Task.Run(() => ThumbnailService.ForceLoad(CurrentADBDevice));
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

        UpdateFileOp();

        Task.Delay(EXPLORER_NAV_DELAY).ContinueWith(_ => App.SafeInvoke(() => RuntimeSettings.IsExplorerLoaded = true));

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath)
    {
        //PasteGrid.Visibility = Visibility.Collapsed;
        FileActions.ListingInProgress = true;

        FileActions.ExplorerFilter = "";
        NavHistory.Navigate(realPath);

        ViewModel.FirstSelectedIndex = -1;
        ViewModel.CurrentSelectedIndex = -1;

        ActiveView.Focus();
        CurrentPath = realPath;

        NavigationBox.Path = realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath;
        ParentPath = FileHelper.GetParentPath(CurrentPath);
        CurrentDrive = DriveHelper.GetCurrentDrive(CurrentPath);

        FileActions.IsRecycleBin = CurrentPath == RECYCLE_PATH;
        FileActions.IsAppDrive = CurrentPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        FileActions.IsTemp = CurrentPath == TEMP_PATH;
        FileActions.ParentEnabled = CurrentPath != ParentPath && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        FileActionLogic.IsPasteEnabled();

        if (!RuntimeSettings.IsRootActive && DevicesObject.Current.Root is AbstractDevice.RootStatus.Enabled)
            RuntimeSettings.IsRootActive = true;

        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current?.Type is not AbstractDevice.DeviceType.Recovery;
        FileActions.UninstallPackageEnabled = false;

        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;

        FileActions.PushFilesFoldersEnabled =
        FileActions.ContextNewEnabled =
        FileActions.ContextPushEnabled =
        FileActions.NewEnabled = !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        FileActions.CopyPathDescription.Value = FileActions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        if (FileActions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync().ContinueWith(_ => DirList.Navigate(realPath));

            FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            FileActions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true);
                FileActionLogic.UpdateFileActions();
                return true;
            }

            DirList.Navigate(realPath);

            FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        RuntimeSettings.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();
        return true;
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
            FileActions.IsRecycleBin = false;
            RuntimeSettings.IsPathBoxFocused = false;
            FileActionLogic.RefreshDrives();
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            if (!FileActions.IsExplorerVisible)
                InitNavigation(location.DisplayName);
            else
                NavigateToPath(location.DisplayName);
        }
    }

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!bfNavigation)
            prevPath = file.FullPath;

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && _navigateToPath(realPath);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        //if (!bfNavigation)
        //    prevPath = path;

        var realPath = FolderHelper.FolderExists(path);
        return realPath is not null && _navigateToPath(realPath);
    }

    private void DriveViewNav()
    {
        FileActionLogic.ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;
        UpdateFileOp();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        CurrentDrive = null;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();

        HomeSavedLocationsList.ItemsSource = NavigationBox.SavedItems;
        if (NavigationBox.SavedItems.Count == 0)
            Settings.HomeLocationsExpanded = false;
    }

    private void UpdateFileOp(bool onlyProgress = true)
    {
        if (!onlyProgress)
            Task.Run(FileActionLogic.UpdateFileOpControls);

        //if (FileOpQ.AnyFailedOperations)
        //    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error;
        //else if (FileOpQ.IsActive)
        //{
        //    if (FileOpQ.Progress == 0)
        //        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
        //    else
        //        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
        //}
        //else
        //    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
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
            return;
        }

        RuntimeSettings.IsPathBoxFocused = false;

        if (!row.IsSelected
            && CopyPaste.DragStatus is not CopyPasteService.DragState.None
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ExplorerGrid.UnselectAll();
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

        if (Settings.DoubleClick is AppSettings.DoubleClickAction.Pull
            && Settings.IsPullOnDoubleClickEnabled
            && FileActions.PullEnabled)
        {
            FileActionLogic.PullFiles(Settings.DefaultFolder);
        }
        else if (Settings.DoubleClick is AppSettings.DoubleClickAction.Edit)
            FileActionLogic.OpenEditor();
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
            return false;

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
        if (DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
            && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder))
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
            DoubleClick(ActiveView.SelectedItem);
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
                    if (ExplorerGrid.SelectedItems.Count == 1 && ExplorerGrid.SelectedItem is FilePath { IsDirectory: true })
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
            RuntimeSettings.DragBitmap = CopyPaste.CurrentFiles.First().DragImage;
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
                WasDragging = false;

                ViewModel.IsMenuOpen = false;
                e.Handled = true;
                return;
            }
        }
        else if (WasDragging)
        {
            WasDragging = false;

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

        if (IsInEditMode)
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

        SelectionRect.Update(point, MouseDownPoint, StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid), ActiveView, ActiveSelectedItems, ViewModel);
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
            vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        if (vfdo is not null)
        {
            CopyPaste.UpdateSelfVFDO(true);
            RuntimeSettings.DragBitmap = selectedItems.First().DragImage;

            vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, dragSource, vfdo.PreferredDropEffect.Value);
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (FileActions.IsAppDrive)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        var sortDirection = ListHelper.Invert(e.Column.SortDirection);
        e.Column.SortDirection = sortDirection;

        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(sortDirection)));

        if (e.Column.SortMemberPath != nameof(FileClass.FullName))
            collectionView.SortDescriptions.Add(new(e.Column.SortMemberPath, sortDirection));

        collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), sortDirection));

        e.Handled = true;
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (ExplorerGrid.SelectedItem is not FileClass file)
            return;

        var textBox = sender as TextBox;

        if (e.Key is Key.Escape or Key.F2)
        {
            var name = FileHelper.DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
            {
                DirList.FileList.Remove(file);
            }
            else
            {
                textBox.Text = FileHelper.DisplayName(textBox);
            }

            file.FolderViewModel.IsInEditMode = false;
            FileActions.IsExplorerEditing = false;
        }
        else if (e.Key is Key.Enter)
        {
            file.FolderViewModel.IsInEditMode = false;
            FileActions.IsExplorerEditing = false;
        }
        else
            return;

        e.Handled = true;
    }

    private void NameColumnEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        var textBox = sender as TextBox;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox.DataContext is not FileClass file || !file.FolderViewModel.IsInEditMode)
            return;

        FileActionLogic.Rename(textBox);

        file.FolderViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        var file = (FileClass)textBox.DataContext;
        textBox.FilterString(CurrentDrive.IsFUSE
            ? INVALID_NTFS_CHARS
            : INVALID_UNIX_CHARS);

        FileActions.IsRenameUnixLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Unix);
        FileActions.IsRenameFuseLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.FUSE);
        FileActions.IsRenameWindowsLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Windows);
        FileActions.IsRenameDriveRootLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.WinRoot);

        var fullName = Settings.ShowExtensions
            ? textBox.Text
            : textBox.Text + file.Extension;

        var comparison = CurrentDrive.IsFUSE
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        FileActions.IsRenameUnique = !DirList.FileList.Except([file]).Any(f => f.FullName.Equals(fullName, comparison));
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
        RuntimeSettings.IsPathBoxFocused = false;
        DriveHelper.ClearSelectedDrives();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RuntimeSettings.IsPathBoxFocused = false;
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        MouseDownPoint = NullPoint;
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
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
            RuntimeSettings.DragBitmap = null;
        else
        {
            if (!FileActions.IsExplorerEditing && RuntimeSettings.IsPathBoxFocused is not true)
                OnButtonKeyDown(sender, e);
        }
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            RuntimeSettings.DragBitmap = null;
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

    private void IconViewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        App.SafeBeginInvoke(() =>
        {
            if (ActiveSelectedItems.Count > 0)
                ActiveScrollIntoView(ActiveSelectedItems[0]);
            else
            {
                var viewer = StyleHelper.FindDescendant<ScrollViewer>(ActiveView);
                viewer?.ScrollToTop();
            }
        }, DispatcherPriority.Loaded);
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);
}
