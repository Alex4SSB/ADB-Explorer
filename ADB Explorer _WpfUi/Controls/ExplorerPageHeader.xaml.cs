using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{
    public ExplorerPageHeader()
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Settings.UICulture;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        InitializeComponent();
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

                case nameof(AppRuntimeSettings.ExplorerSource):
                    ExplorerGrid.ItemsSource = RuntimeSettings.ExplorerSource;
                    FilterExplorerItems();
                    break;

                default:
                    break;
            }
        });
    }

    private void FilterExplorerItems(bool refreshOnly = false)
    {
        if (!FileActions.IsExplorerVisible)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        if (collectionView is null)
            return;

        if (refreshOnly)
            collectionView.Refresh();

        if (FileActions.IsAppDrive)
        {
            collectionView.Filter = Settings.ShowSystemPackages
                ? FileHelper.PkgFilter()
                : pkg => ((Package)pkg).Type is Package.PackageType.User;

            if (collectionView.SortDescriptions.All(d => d.PropertyName != nameof(Package.Type)))
            {
                ExplorerGrid.Columns[8].SortDirection = ListSortDirection.Descending;

                collectionView.SortDescriptions.Add(new(nameof(Package.Type), ListSortDirection.Descending));
            }
        }
        else
        {
            collectionView.Filter = !Settings.ShowHiddenItems
                ? FileHelper.HideFiles()
                : file => !FileHelper.IsHiddenRecycleItem((FileClass)file);

            if (!collectionView.SortDescriptions.Any(d => d.PropertyName
                    is nameof(FileClass.IsTemp)
                    or nameof(FileClass.IsDirectory)
                    or nameof(FileClass.SortName)))
            {
                ExplorerGrid.Columns[1].SortDirection = ListSortDirection.Ascending;

                collectionView.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
                collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListSortDirection.Descending));
                collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), ListSortDirection.Ascending));
            }
        }
    }

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
                //UnfinishedBlock.Visible(DirList.IsProgressVisible);
                //NavigationBox.IsLoadingProgressVisible = DirList.IsProgressVisible;
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

        //NavigationBox.Path = realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath;
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

        OriginalPath.Visibility =
        OriginalDate.Visibility = Visible(FileActions.IsRecycleBin);

        PackageName.Visibility =
        PackageType.Visibility =
        PackageUid.Visibility =
        PackageVersion.Visibility = Visible(FileActions.IsAppDrive);

        IconColumn.Visibility =
        NameColumn.Visibility =
        DateColumn.Visibility =
        TypeColumn.Visibility =
        SizeColumn.Visibility = Visible(!FileActions.IsAppDrive);

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

        //NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        //NavigationBox.Path = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        DriveList.ItemsSource = DevicesObject.Current.Drives;
        CurrentDrive = null;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();

        //HomeSavedLocationsList.ItemsSource = NavigationBox.SavedItems;
        //if (NavigationBox.SavedItems.Count == 0)
        //    Settings.HomeLocationsExpanded = false;
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
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
    }

    private void DataGridCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
    }

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
    }

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
    }

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
    }
}
