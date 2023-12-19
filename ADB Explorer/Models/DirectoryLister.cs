using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models;

public class DirectoryLister : ViewModelBase
{
    public ADBService.AdbDevice Device { get; }
    public ObservableList<FileClass> FileList { get; }

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

    private bool isLinlListingFinished = false;
    public bool IsLinkListingFinished
    {
        get => isLinlListingFinished;
        set => Set(ref isLinlListingFinished, value);
    }

    private Dispatcher Dispatcher { get; }
    private Task UpdateTask { get; set; }
    private TimeSpan UpdateInterval { get; set; }
    private int MinUpdateThreshold { get; set; }
    private Task ReadTask { get; set; }
    private CancellationTokenSource CurrentCancellationToken { get; set; }
    private CancellationTokenSource LinkListCancellation { get; set; }
    private Func<FileClass, FileClass> FileManipulator { get; }

    private ConcurrentQueue<FileStat> currentFileQueue;

    public DirectoryLister(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, Func<FileClass, FileClass> fileManipulator = null)
    {
        Dispatcher = dispatcher;
        FileList = new();
        Device = adbDevice;
        FileManipulator = fileManipulator;
    }

    public void Navigate(string path)
    {
        StartDirectoryList(path);
    }

    public void Stop()
    {
        LinkListCancellation?.Cancel();
        StopDirectoryList();
    }

    private void StartDirectoryList(string path)
    {
        Dispatcher.BeginInvoke(() =>
        {
            IsLinkListingFinished = false;

            LinkListCancellation?.Cancel();
            StopDirectoryList();
            FileList.RemoveAll();

            InProgress = true;
            IsProgressVisible = false;
            CurrentPath = path;
        }).Wait();

        CurrentCancellationToken = new();
        LinkListCancellation = new();
        currentFileQueue = new ConcurrentQueue<FileStat>();

        ReadTask = Task.Run(() => Device.ListDirectory(CurrentPath, ref currentFileQueue, CurrentCancellationToken.Token), CurrentCancellationToken.Token);
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

        Task.Run(ListLinks, LinkListCancellation.Token);
    }

    private async void ListLinks()
    {
        await AsyncHelper.WaitUntil(() => FileList.Count > 0, DIR_LIST_UPDATE_INTERVAL, TimeSpan.FromMilliseconds(20), LinkListCancellation.Token);

        var items = FileList.Where(f => f.IsLink && f.Type is FileType.Unknown).ToList();
        if (items.Count < 1)
        {
            IsLinkListingFinished = true;
            return;
        }

        IEnumerable<(string, FileType)> result = null;
        try
        {
            result = Device.GetLinkType(items.Select(f => f.FullPath), LinkListCancellation.Token);
        }
        catch (AggregateException e) when (e.InnerException is TaskCanceledException)
        { }

        if (result is null)
        {
            IsLinkListingFinished = true;
            return;
        }

        foreach (var item in result)
        {
            var file = items.Where(f => f.FullPath == item.Item1);
            if (!file.Any())
                continue;

            Dispatcher.Invoke(() =>
            {
                file.First().Type = item.Item2;
                file.First().UpdateType();
            });
        }

        IsLinkListingFinished = true;

        Data.RuntimeSettings.RefreshExplorerSorting = true;
    }
}
