using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls;

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
    private FileToIconConverter FileToIcon;
    private DateTime appDataClick;
    //private readonly DragWindow dw = new();

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
            if (FileActions.IsAppDrive || !ExplorerGrid.SelectedCells.Any())
                return false;

            var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
            return cell switch
            {
                null => false,
                _ => cell.IsEditing
            };
        }
        set
        {
            var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
            if (cell is not null)
            {
                cell.IsEditing = value;
                FileActions.IsExplorerEditing = value;
            }
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
        });

        SelectionTimer.Tick += SelectionTimer_Tick;
    }

    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        //RuntimeSettings.IsDevicesPaneOpen
        //    || RuntimeSettings.IsSettingsPaneOpen
        //    ||
        if (SearchBox.IsFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ExplorerGrid.SelectedItems.Count;
        var selectedIndex = ExplorerGrid.SelectedIndex;
        object altItem = null;

        for (int i = 0; i < ExplorerGrid.Items.Count; i++)
        {
            var item = ExplorerGrid.Items[i];
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

        if (selectedIndex == ExplorerGrid.SelectedIndex && altItem is not null)
            FileActions.ItemToSelect = altItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        //if (RuntimeSettings.IsSettingsPaneOpen || RuntimeSettings.IsDevicesPaneOpen)
        //    return;

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
        if (DriveList.Items.Count == 0) // || RuntimeSettings.IsSettingsPaneOpen || RuntimeSettings.IsDevicesPaneOpen
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
        if (ExplorerGrid.Items.Count < 1) // || RuntimeSettings.IsSettingsPaneOpen || RuntimeSettings.IsDevicesPaneOpen
            return false;

        switch (key)
        {
            case Key.Escape:
                ExplorerGrid.UnselectAll();
                break;

            case Key.Down or Key.Up or Key.Home or Key.End:
                if (bfNavigation)
                {
                    SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                    bfNavigation = false;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    ExplorerGrid.MultiSelect(key);
                else
                    ExplorerGrid.SingleSelect(key);
                break;

            case Key.Enter:
                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode)
                    return false;

                if (ExplorerGrid.SelectedItems.Count == 1 && ((FilePath)ExplorerGrid.SelectedItem).IsDirectory)
                    DoubleClick(ExplorerGrid.SelectedItem);
                break;

            case Key.Apps:
                ExplorerGrid.ContextMenu.IsOpen = true;
                break;

            default:
                return false;
        }

        return true;
    }

    private void SelectionTimer_Tick(object sender, EventArgs e)
    {
        SelectedFiles = FileActions.IsAppDrive ? [] : ExplorerGrid.SelectedItems.OfType<FileClass>();
        SelectedPackages = FileActions.IsAppDrive ? ExplorerGrid.SelectedItems.OfType<Package>() : [];
        FileActions.SelectedItemsCount = FileActions.IsAppDrive ? SelectedPackages.Count() : SelectedFiles.Count();

        FileActionLogic.UpdateFileActions();
        //MainToolBar.Items?.Refresh();
        //PasteGrid.Visibility = Visibility.Visible;

        SelectionTimer.Stop();
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
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

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    if (RuntimeSettings.LocationToNavigate is null)
                        return;

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

                case nameof(AppRuntimeSettings.IsPathBoxFocused):
                    IsPathBoxFocused(RuntimeSettings.IsPathBoxFocused
                                     ?? NavigationBox.Mode
                                     is NavigationBox.ViewMode.Breadcrumbs);

                    RuntimeSettings.AutoHideSearchBox = true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    if (ExplorerGrid.Items.Count == ExplorerGrid.SelectedItems.Count)
                        ExplorerGrid.UnselectAll();
                    else
                        ExplorerGrid.SelectAll();
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

            NavigationBox.UnfocusTarget = ExplorerGrid;
            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        }
    }

    private void FilterFileActions() => Dispatcher.Invoke(() => MainToolBar.Items?.Refresh());

    private void FilterExplorerContextMenu() => Dispatcher.Invoke(() =>
    {
        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ContextMenu.ItemsSource);
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

    private void InitLister()
    {
        DirList = new(Dispatcher, CurrentADBDevice, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(() =>
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

                        Dispatcher.Invoke(() => FileActions.ListingInProgress = DirList.InProgress);
                    });

                    if (DirList.InProgress)
                        return;

                    if (FileActions.IsRecycleBin)
                    {
                        TrashHelper.EnableRecycleButtons();
                    }

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when ExplorerGrid.Items.Count < 1 || !DirList.IsLinkListingFinished:
                return;

            //case nameof(DirectoryLister.IsLinkListingFinished) when bfNavigation
            //    && !string.IsNullOrEmpty(prevPath) && DirList.FileList.FirstOrDefault(item => item.FullPath == prevPath) is var prevItem and not null:
            //    FileActions.ItemToSelect = prevItem;
            //    break;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    if (ExplorerGrid.Items.Count > 0)
                        ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
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

        Task.Delay(EXPLORER_NAV_DELAY).ContinueWith(_ => Dispatcher.Invoke(() => RuntimeSettings.IsExplorerLoaded = true));

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath)
    {
        //PasteGrid.Visibility = Visibility.Collapsed;
        FileActions.ListingInProgress = true;

        FileActions.ExplorerFilter = "";
        NavHistory.Navigate(realPath);

        SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, -1);
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, -1);

        ExplorerGrid.Focus();
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

    private void NavigateToLocation(AdbLocation location)
    {
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);

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
        WasEditing = cell.IsEditing;

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

        SelectionHelper.SetNextSelectedIndex(ExplorerGrid, current);
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, current);
        if (ExplorerGrid.SelectedItems.Count < 1)
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, current);
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

        if (SelectionRect.IsVisible)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
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

                    if (cell.IsEditing)
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
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, current);

        if (MultiRowSelect(row))
            return true;

        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, current);
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
        if (cell.IsReadOnly
            || (DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder)))
            return;

        var path = ((FileClass)ExplorerGrid.SelectedItem).FullPath;

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

                    var currentPath = Dispatcher.Invoke(() => ((FileClass)ExplorerGrid.SelectedItem)?.FullPath);
                    if (ClickCount > 1 || currentPath != path)
                        return;
                }

                Dispatcher.Invoke(() =>
                {
                    cell.IsEditing = true;
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

            var firstSelected = SelectionHelper.GetFirstSelectedIndex(ExplorerGrid);
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

        SelectionHelper.SetIndexSingle(ExplorerGrid, row.GetIndex());
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !FileActions.IsAppDrive && SelectedFiles.Count() == 1 && !IsInEditMode)
            DoubleClick(ExplorerGrid.SelectedItem);
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
                ExplorerGrid.MultiSelect(key);
                break;

            case Key.Up or Key.Down:
                ExplorerGrid.SingleSelect(key);
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
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var point = Mouse.GetPosition(ExplorerGrid);
        if (point.Y < ColumnHeaderHeight || WasDragging)
        {
            WasDragging = false;

            SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);
            e.Handled = true;
            return;
        }

        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, true);
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

        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, selectionIndex);

        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, selectionIndex);
        }
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(ExplorerCanvas);
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
            || SelectionHelper.GetIsMenuOpen(ExplorerGrid.ContextMenu);

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending && (MouseDownPoint - point).LengthSquared >= 25)
        {
            if (ExplorerGrid.SelectedItems.Count > 0
                && ExplorerGrid.SelectedItems[0] is FileClass or Package
                && !abortDrag)
            {
                CopyPaste.DragStatus = CopyPasteService.DragState.Active;
                WasDragging = true;

                IEnumerable<FileClass> selectedItems;
                VirtualFileDataObject vfdo;
                if (FileActions.IsAppDrive)
                {
                    vfdo = VirtualFileDataObject.PrepareTransfer(ExplorerGrid.SelectedItems.Cast<Package>());
                    selectedItems = VirtualFileDataObject.SelfFiles;
                }
                else
                {
                    selectedItems = ExplorerGrid.SelectedItems.Cast<FileClass>();
                    vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                }

                if (vfdo is not null)
                {
                    CopyPaste.UpdateSelfVFDO(true);
                    RuntimeSettings.DragBitmap = FileToIconConverter.GetBitmapSource(selectedItems.First());

                    vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, cell, vfdo.PreferredDropEffect.Value);
                }
            }
            else
                CopyPaste.DragStatus = CopyPasteService.DragState.None;
        }

        if (abortDrag || CopyPaste.DragStatus is not CopyPasteService.DragState.None || WasDragging)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }
        var scroller = StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
        var horizontal = scroller.ComputedHorizontalScrollBarVisibility is Visibility.Visible ? 1 : 0;
        var vertical = scroller.ComputedVerticalScrollBarVisibility is Visibility.Visible ? 1 : 0;

        if (!SelectionRect.IsVisible
            && ((point.Y > ExplorerCanvas.ActualHeight - SystemParameters.HorizontalScrollBarHeight * horizontal)
            || (point.X > ExplorerCanvas.ActualWidth - SystemParameters.VerticalScrollBarWidth * vertical)))
        {
            MouseDownPoint = point;
        }

        if (MouseDownPoint.Y > ExplorerCanvas.ActualHeight - SystemParameters.HorizontalScrollBarHeight * horizontal
            || MouseDownPoint.X > ExplorerCanvas.ActualWidth - SystemParameters.VerticalScrollBarWidth * vertical)
            return;

        SelectionRect.Visibility = Visibility.Visible;
        if (point.Y > MouseDownPoint.Y)
        {
            Canvas.SetTop(SelectionRect, MouseDownPoint.Y);
        }
        else
        {
            Canvas.SetTop(SelectionRect, point.Y);
        }
        if (point.X > MouseDownPoint.X)
        {
            Canvas.SetLeft(SelectionRect, MouseDownPoint.X);
        }
        else
        {
            Canvas.SetLeft(SelectionRect, point.X);
        }

        SelectionRect.Height = Math.Abs(MouseDownPoint.Y - point.Y);
        SelectionRect.Width = Math.Abs(MouseDownPoint.X - point.X);

        SelectRows(point);
    }

    private void SelectRows(Point mousePosition)
    {
        Rect selection = new(Canvas.GetLeft(SelectionRect),
                             Canvas.GetTop(SelectionRect),
                             SelectionRect.Width,
                             SelectionRect.Height);

        for (int i = 0; i < ExplorerGrid.ItemContainerGenerator.Items.Count; i++)
        {
            if (ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                continue;

            Rect rowRect = new(row.TranslatePoint(new(), ExplorerGrid), row.DesiredSize);
            row.IsSelected = rowRect.IntersectsWith(selection);

            rowRect.Inflate(double.PositiveInfinity, 0);
            if (rowRect.Contains(mousePosition))
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, row.GetIndex());
        }

        if (ExplorerGrid.SelectedItems.Count == 1
            && (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift))
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExplorerGrid.SelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ExplorerGrid.UnselectAll();
            return;
        }

        if (!SelectionHelper.GetSelectionInProgress(ExplorerGrid))
        {
            if (ExplorerGrid.SelectedItems.Count == 1)
            {
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                }
            }
            else if (ExplorerGrid.SelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.Items.IndexOf(e.AddedItems[0]));
            }
        }

        SelectionTimer.Stop();
        SelectionTimer.Start();
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
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
        var textBox = sender as TextBox;

        if (e.Key is Key.Escape or Key.F2)
        {
            var name = FileHelper.DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
            {
                DirList.FileList.Remove(ExplorerGrid.SelectedItem as FileClass);
            }
            else
            {
                textBox.Text = FileHelper.DisplayName(sender as TextBox);
            }

            AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
        }
        else if (e.Key is not Key.Enter)
            return;

        e.Handled = true;

        if (ExplorerGrid.SelectedCells.Count > 0)
        {
            cell.IsEditing = false;
            FileActions.IsExplorerEditing = false;
        }
    }

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        textBox.Focus();

        var editPoint = textBox.TranslatePoint(new(), ExplorerCanvas);
        //Canvas.SetTop(RenameTooltip, editPoint.Y - RenameTooltip.ActualHeight - 4);
        //Canvas.SetLeft(RenameTooltip, editPoint.X + 4);
        //RenameTooltip.Visibility = Visibility.Visible;
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        FileActionLogic.Rename(sender as TextBox);
        //RenameTooltip.Visibility = Visibility.Hidden;
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

    private void ExplorerCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SelectionRect.Visibility = Visibility.Collapsed;

        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, SelectionHelper.GetNextSelectedIndex(ExplorerGrid));
        }
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

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);
}
