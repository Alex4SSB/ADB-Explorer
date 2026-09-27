using static ADB_Explorer.Models.AbstractFile;

using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPageContent
{
    // Requests are app-wide (raised by whatever the user just clicked on their current tab),
    // so a cached background tab's content must not act on them too.
    private bool IsActiveInstance => ReferenceEquals(Instance, Data.ActiveExplorerInstance);

    private void SubscribeRequests()
    {
        Data.ExplorerRequested += OnExplorerRequested;
        Data.NavigationRequested += OnNavigationRequested;
        Data.PathNavigationRequested += OnPathNavigationRequested;
        Data.BrowseDriveRequested += OnBrowseDriveRequested;
        Data.CompressToRequested += OnCompressToRequested;
    }

    private void OnExplorerRequested(object? sender, ExplorerRequest request)
    {
        if (!IsActiveInstance)
            return;

        App.SafeInvoke(() =>
        {
            switch (request)
            {
                case ExplorerRequest.DriveViewNav:
                    if (!DiskUsagePollingService.ServerUnresponsive)
                        DriveViewNav();
                    break;

                case ExplorerRequest.InitLister:
                    InitLister();
                    break;

                case ExplorerRequest.FilterActions:
                    Task.Run(() =>
                    {
                        if (Instance.FileList.Actions.IsAppDrive || Instance.FileList.Actions.IsRecycleBin || Instance.EffectiveDevice is null)
                            FilterFileActions();
                    });
                    Task.Run(() => ExplorerContextMenu.UpdateSeparators());
                    break;

                case ExplorerRequest.NewFolder:
                    NewItem(true);
                    break;

                case ExplorerRequest.NewFile:
                    NewItem(false);
                    break;

                case ExplorerRequest.PasteClipboardImage:
                    NewImagePasteItem();
                    break;

                case ExplorerRequest.Rename:
                    if (Instance.FileList.Actions.RenameEnabled)
                        ExplorerList.IsInEditMode ^= true;
                    break;

                case ExplorerRequest.SelectAll:
                    ExplorerList.ToggleSelectAll();
                    break;

                case ExplorerRequest.ToggleSearch:
                    Instance.IsSearchExpanded ^= true;
                    if (Instance.IsSearchExpanded)
                        ExplorerList.ClearSelectionForSearch();
                    break;

                default:
                    break;
            }
        });
    }

    private void OnNavigationRequested(object? sender, AdbLocation requested)
    {
        if (!IsActiveInstance)
            return;

        App.SafeInvoke(() =>
        {
            // Pages have no listing to stop, and the explorer under them stays as it is.
            if (requested.IsPage)
            {
                BfNavigation = false;
                NavigateToLocation(requested);
                return;
            }

            switch (requested.Location)
            {
                case Navigation.SpecialLocation.Back:
                    BfNavigation = true;
                    NavigateHistoryEntry(Instance.History.GoBack());
                    break;
                case Navigation.SpecialLocation.Forward:
                    BfNavigation = true;
                    NavigateHistoryEntry(Instance.History.GoForward());
                    break;
                case Navigation.SpecialLocation.Up:
                    StopListing();
                    BfNavigation = false;
                    if (Instance.FileList.Actions.IsSearchMode)
                        ExitSearchMode();
                    else
                        NavigateToPath(ParentPath);
                    break;
                default:
                    BfNavigation = false;
                    if (!IsRevealingHiddenExplorer(requested))
                        StopListing();

                    if (Instance.FileList.Actions.IsDriveViewVisible && requested.Location is Navigation.SpecialLocation.DriveView && !Instance.IsShowingPage)
                        FileActionLogic.RefreshDrives(true, DeviceCts.Token, Instance.EffectiveDevice);
                    else
                        NavigateToLocation(requested);
                    break;
            }
        });
    }

    private void OnPathNavigationRequested(object? sender, string path)
    {
        if (!IsActiveInstance)
            return;

        App.SafeInvoke(() =>
        {
            if (path == "-")
            {
                BfNavigation = true;
                NavigateHistoryEntry(Instance.History.GoBack());
            }
            else if (Instance.FileList.Actions.IsExplorerVisible)
                NavigateToLocation(new(path));
            else if (!InitNavigation(path))
                DriveViewNav();
        });
    }

    private void OnBrowseDriveRequested(object? sender, DriveViewModel drive)
    {
        if (IsActiveInstance)
            App.SafeInvoke(() => InitNavigation(drive.Path));
    }

    private void OnCompressToRequested(object? sender, string extension)
    {
        if (IsActiveInstance)
            App.SafeInvoke(() => NewCompressItem(extension));
    }

    private void FilterFileActions() => App.SafeInvoke(() => Chrome.MainToolBar.Items?.Refresh());

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        Instance.FileList.DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode) // in case the editing element was not acquired
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewCompressItem(string extension)
    {
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

        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, $"{baseName}{extension}");
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingCompressTemp(newItem);

        Instance.FileList.DirList.FileList.Insert(0, newItem);

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

        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, FileActionLogic.GetClipboardImageFileName());
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingClipboardImageTemp(newItem);

        Instance.FileList.DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        if (Instance.IsIconView && FileActionLogic.GetPendingClipboardImageThumbnail() is { } preview)
            newItem.IconViewModel.SetImmediatePreview(preview);

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void SortExplorer()
    {
        if (Settings.SortingPerLocation && Settings.LocationSorting.TryGetValue(Instance.FileList.Path, out var sort))
        {
            if (Instance.FileList.Actions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.Date or SortingSelector.SortingProperty.Size)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else if (!Instance.FileList.Actions.IsAppDrive
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
        if (Instance.FileList.Actions.IsDriveViewVisible)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
            return;
        }

        if (Instance.FileList.Actions.IsSearchMode)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Content;
            return;
        }

        if (string.IsNullOrEmpty(Instance.FileList.Path))
            return;

        if (Instance.FileList.Actions.IsAppDriveThumbsLocked)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Disabled;
            return;
        }

        if (Settings.ThumbSizePerLocation)
        {
            ThumbnailService.ThumbnailSize size = ThumbnailService.ThumbnailSize.Disabled;
            Settings.LocationThumbSize.TryGetValue(Instance.FileList.Path, out size);
            Instance.CurrentThumbsSize = size;
        }
        else
        {
            Instance.CurrentThumbsSize = RuntimeSettings.BrowseThumbsSize;
        }

        if (Instance.FileList.Actions.IsAppDrive
            && Data.Packages is { Count: > 0 }
            && ApkIconService.IsEnabled)
            ApkIconService.BeginPreloadPackages(Data.Packages);
    }

    internal void InvalidateFileIcons()
    {
        if (Instance.FileList.DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
            file.InvalidateIconViewModelThumbnail();
    }

    internal void DisposeFileIcons()
    {
        if (Instance.FileList.DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
        {
            file.DisposeIconViewModel();
            file.CancelFolderSizeCalculation();
        }

        Task.Run(static () =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        });
    }
}
