using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models;

public class DirectoryLister(Dispatcher dispatcher, LogicalDeviceViewModel device, Func<FileClass, FileClass> fileManipulator = null) : ViewModelBase
{
    public LogicalDeviceViewModel Device { get; } = device;
    public ObservableList<FileClass> FileList { get; } = [];

    private string currentPath;
    public string CurrentPath
    {
        get => currentPath;
        private set => Set(ref currentPath, value);
    }

    private bool inProgress;
    public bool InProgress
    { 
        get => inProgress;
        private set => Set(ref inProgress, value);
    }

    private bool isProgressVisible = false;
    public bool IsProgressVisible
    {
        get => isProgressVisible;
        private set => Set(ref isProgressVisible, value);
    }

    private bool isLinkListingFinished = false;
    public bool IsLinkListingFinished
    {
        get => isLinkListingFinished;
        private set => Set(ref isLinkListingFinished, value);
    }

    private Dispatcher Dispatcher { get; } = dispatcher;
    private Task UpdateTask { get; set; }
    private TimeSpan UpdateInterval { get; set; }
    private int MinUpdateThreshold { get; set; }
    private Task ReadTask { get; set; } = null;
    private CancellationTokenSource CurrentCancellationToken { get; set; }
    private CancellationTokenSource LinkListCancellation { get; set; }
    private Func<FileClass, FileClass> FileManipulator { get; } = fileManipulator;

    private ConcurrentQueue<FileStat> currentFileQueue;

    private FileClass? locationSource;

    [ObservableProperty]
    public partial FileClass? CurrentLocation { get; private set; }

    public void Navigate(string path, FileClass? locationSource = null)
    {
        this.locationSource = locationSource;
        StartDirectoryList(path);
    }

    public void RefreshLocationAccess()
    {
        if (string.IsNullOrEmpty(currentPath))
            return;

        var path = currentPath;
        var source = CurrentLocation;
        var token = LinkListCancellation?.Token ?? Data.DeviceCts.Token;

        Task.Run(() => UpdateLocationAccess(path, source, token), token);
    }

    public void ClearCurrentLocation() => CurrentLocation = null;

    public void Stop()
    {
        LinkListCancellation?.Cancel();
        StopDirectoryList();
        IsLinkListingFinished = true;
    }

    private void StartDirectoryList(string path)
    {
        FileClass? source;
        Dispatcher.BeginInvoke(() =>
        {
            IsLinkListingFinished = false;

            LinkListCancellation?.Cancel();
            StopDirectoryList();
            FileList.RemoveAll();

            InProgress = true;
            IsProgressVisible = false;
            CurrentPath = path;

            source = locationSource;
            locationSource = null;

            var restrictions = DriveHelper.GetCurrentDrive(path)?.Restrictions ?? DriveRestrictions.None;
            var preliminary = FileClass.BuildCurrentLocation(path, null, source, Device.ShellIdentity, restrictions);
            CurrentLocation = preliminary;
        }).Wait();

        CurrentCancellationToken = new();
        LinkListCancellation = new();
        currentFileQueue = new ConcurrentQueue<FileStat>();

        ReadTask = Task.Run(() =>
            ADBService.ListDirectory(Device.ID, path, ref currentFileQueue, Dispatcher, CurrentCancellationToken.Token),
            CurrentCancellationToken.Token);
        ReadTask.ContinueWith((t) => Dispatcher.BeginInvoke(() => StopDirectoryList()), CurrentCancellationToken.Token);

        Task.Delay(DIR_LIST_VISIBLE_PROGRESS_DELAY).ContinueWith((t) => Dispatcher.BeginInvoke(() => IsProgressVisible = InProgress), CurrentCancellationToken.Token);

        ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        UpdateDelays(currentFileQueue.Count);

        UpdateTask = Task.Delay(UpdateInterval);
        UpdateTask.ContinueWith(
            (t) => Dispatcher.BeginInvoke(() => UpdateDirectoryList(!InProgress)),
            CurrentCancellationToken.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void UpdateDelays(int queueCount)
    {
        bool manyPendingFilesExist = queueCount >= DIR_LIST_UPDATE_THRESHOLD_MAX;
        bool isListingStarting = FileList.Count < DIR_LIST_START_COUNT;

        if (isListingStarting || manyPendingFilesExist)
        {
            UpdateInterval = DIR_LIST_UPDATE_START_INTERVAL;
            MinUpdateThreshold = DIR_LIST_UPDATE_START_THRESHOLD_MIN;
        }
        else
        {
            UpdateInterval = DIR_LIST_UPDATE_INTERVAL;
            MinUpdateThreshold = DIR_LIST_UPDATE_THRESHOLD_MIN;
        }
    }

    private void UpdateDirectoryList(bool finish)
    {
        if (finish || (currentFileQueue.Count >= MinUpdateThreshold))
        {
            for (int i = 0; finish || (i < DIR_LIST_UPDATE_THRESHOLD_MAX); i++)
            {
                if (!currentFileQueue.TryDequeue(out FileStat fileStat))
                {
                    break;
                }

                FileClass item = FileClass.GenerateAndroidFile(fileStat);

                if (FileManipulator is not null)
                {
                    item = FileManipulator(item);
                }

                FileList.Add(item);
            }
        }

        if (!finish)
        {
            ScheduleUpdate();
        }
    }

    private void StopDirectoryList()
    {
        if (ReadTask == null)
        {
           return;
        }

        CurrentCancellationToken.Cancel();

        try
        {
            ReadTask.Wait();
        }
        catch (AggregateException e) when (e.InnerException is TaskCanceledException)
        { }

        UpdateDirectoryList(true);

        InProgress = false;
        IsProgressVisible = false;
        ReadTask = null;
        CurrentCancellationToken = null;

        var path = currentPath;
        var source = CurrentLocation;
        var token = LinkListCancellation.Token;

        if (currentFileQueue.IsEmpty && !FileList.Any())
        {
            IsLinkListingFinished = true;
            Task.Run(() => UpdateLocationAccess(path, source, token), token);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(
                    ListLinksAsync(token),
                    Task.Run(() => UpdateLocationAccess(path, source, token), token));
            }
            catch (OperationCanceledException)
            { }
        }, token);
    }

    private void UpdateLocationAccess(string path, FileClass? source, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var identity = Device.GetOrLoadShellIdentity();
        var restrictions = DriveHelper.GetCurrentDrive(path)?.Restrictions ?? DriveRestrictions.None;
        var info = ADBService.GetLocationInfo(Device.ID, path, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;

        var location = FileClass.BuildCurrentLocation(path, info, source, identity, restrictions);

        Dispatcher.Invoke(() =>
        {
            if (path != currentPath)
                return;

            CurrentLocation = location;
            FileActionLogic.UpdateFileActions();
        });
    }

    private async Task ListLinksAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AsyncHelper.WaitUntil(() => FileList.Count > 0, DIR_LIST_UPDATE_INTERVAL, TimeSpan.FromMilliseconds(20), cancellationToken);

            var items = FileList.Where(f => f.IsLink && f.Type is FileType.Unknown).ToList();
            if (items.Count < 1)
                return;

            List<(string, FileType)> result;
            try
            {
                result = [.. ADBService.GetLinkType(Device.ID, items.Select(f => f.FullPath), cancellationToken)];
            }
            catch (OperationCanceledException)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var file = items[i];
                var target = result[i];

                Dispatcher.Invoke(() =>
                {
                    file.LinkTarget = target.Item1;
                    file.Type = target.Item2;
                    file.UpdateType();
                });
            }
        }
        finally
        {
            Dispatcher.BeginInvoke(() => IsLinkListingFinished = true);
        }
    }
}
