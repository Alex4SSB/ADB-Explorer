using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPageContent
{
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>Set while a history entry's query is put in the search box, which must not start a second search of its own.</summary>
    private bool _isRestoringSearch;

    /// <summary>Back / forward landed on a search entry: put its query back and search again from its folder.</summary>
    private void RestoreSearch(AdbLocation location)
    {
        if (string.IsNullOrEmpty(location.SearchQuery) || string.IsNullOrEmpty(location.SearchOrigin))
        {
            if (!string.IsNullOrEmpty(Instance.FileList.Actions.ExplorerFilter))
                RunExplorerSearch();

            return;
        }

        _isRestoringSearch = true;

        try
        {
            Instance.FileList.Actions.ExplorerFilter = location.SearchQuery;
        }
        finally
        {
            _isRestoringSearch = false;
        }

        RunExplorerSearch(location);
    }

    private void SetSearchOrigin(string origin)
    {
        var list = Instance.FileList;
        list.SearchOriginPath = origin;

        var deviceId = Instance.EffectiveDevice?.ID;
        list.SearchOriginCanWrite = list.DirList?.CurrentLocation is { CanWriteLocation: true } location
            && location.FullPath == origin
            || deviceId is not null && DriveHelper.IsModificationAllowedAt(origin, deviceId);
    }

    private void RunExplorerSearch(AdbLocation? restore = null)
    {
        if (Instance.EffectiveDevice is null)
            return;

        if (restore is not null && Instance.FileList.DirList is null)
            InitLister();

        if (Instance.FileList.DirList is null)
            return;

        // A restored search brings its own settings, folder and view along, so none of these can rule it out.
        if (restore is null
            && (Settings.SearchBox is not SearchBox.SearchBoxMode.AllSubfolders
                || !Instance.FileList.Actions.IsExplorerVisible
                || Instance.FileList.Actions.IsAppDrive
                || Instance.FileList.Actions.IsRecycleBin))
        {
            return;
        }

        var query = Instance.FileList.Actions.ExplorerFilter?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (Instance.FileList.Actions.IsSearchMode)
                ExitSearchMode();
            return;
        }

        if (restore?.SearchOrigin is { Length: > 0 } restoredOrigin)
        {
            SetSearchOrigin(restoredOrigin);
            Instance.FileList.CurrentDrive = DriveHelper.GetCurrentDrive(restoredOrigin);
        }
        else if (!Instance.FileList.Actions.IsSearchMode)
            SetSearchOrigin(Instance.FileList.Path);

        var searchRoot = Instance.FileList.SearchOriginPath;
        if (string.IsNullOrEmpty(searchRoot)
            || AdbLocation.LocationFromString(searchRoot) is not Navigation.SpecialLocation.None)
        {
            return;
        }

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Instance.FileList.DirList.Stop();
        DisposeFileIcons();

        Instance.FileList.Actions.ListingInProgress = true;
        Instance.FileList.Actions.IsSearchMode = true;
        Instance.FileList.Actions.IsDriveViewVisible = false;
        Instance.FileList.Actions.IsExplorerVisible = true;
        Instance.FileList.Actions.HomeEnabled = true;
        Instance.FileList.Actions.IsRecycleBin = false;
        Instance.FileList.Actions.IsAppDrive = false;
        Instance.FileList.Actions.IsArchive = false;
        Instance.FileList.Actions.IsTemp = false;
        Instance.FileList.Actions.ParentEnabled = false;

        var searchPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.SearchMode);
        CurrentPath = searchPath;
        SetExplorerBoxPath(searchPath);
        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

        Instance.History.NavigateSearch(AdbLocation.ForSearch(query, searchRoot));
        Instance.NotifyTabChanged();

        Instance.FirstSelectedIndex = -1;
        Instance.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.SelectedFiles = [];

        ApplyLocationThumbSize();

        SortExplorer();
        Instance.FileList.DirList.Search(searchRoot, query, DeviceCts.Token);
        Instance.ExplorerSource = Instance.FileList.DirList.FileList;
        FileActionLogic.UpdateFileActions();
        ExplorerList.ResetExplorerHorizontalScroll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.RefreshSelection();
    }

    private void ExitSearchMode()
    {
        if (!Instance.FileList.Actions.IsSearchMode)
            return;

        var origin = Instance.FileList.SearchOriginPath;
        Instance.FileList.Actions.IsSearchMode = false;
        Instance.FileList.SearchOriginPath = null;
        Instance.FileList.SearchOriginCanWrite = false;
        Instance.FileList.SearchTransferParent = null;
        Instance.FileList.Actions.ExplorerFilter = "";

        if (!string.IsNullOrEmpty(origin))
            NavigateToPath(origin);
    }
}
