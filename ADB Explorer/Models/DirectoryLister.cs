using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
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

    private Dispatcher Dispatcher { get; }
    private Task UpdateTask { get; set; }
    private TimeSpan UpdateInterval { get; set; }
    private int MinUpdateThreshold { get; set; }
    private Task ReadTask { get; set; }
    private CancellationTokenSource CurrentCancellationToken { get; set; }
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
        StopDirectoryList();
    }

    private void StartDirectoryList(string path)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StopDirectoryList();
            FileList.RemoveAll();

            InProgress = true;
            IsProgressVisible = false;
            CurrentPath = path;
        }).Wait();

        CurrentCancellationToken = new CancellationTokenSource();
        currentFileQueue = new ConcurrentQueue<FileStat>();
        ReadTask = Task.Run(() => Device.ListDirectory(CurrentPath, ref currentFileQueue, CurrentCancellationToken.Token), CurrentCancellationToken.Token);
        ReadTask.ContinueWith((t) => Dispatcher.BeginInvoke(StopDirectoryList), CurrentCancellationToken.Token);
        
        Task.Delay(DIR_LIST_VISIBLE_PROGRESS_DELAY).ContinueWith((t) => Dispatcher.BeginInvoke(() => IsProgressVisible = InProgress), CurrentCancellationToken.Token);

        ScheduleUpdate();
    }

    private void ScheduleUpdate()
    {
        bool manyPendingFilesExist = currentFileQueue.Count >= DIR_LIST_UPDATE_THRESHOLD_MAX;
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

        UpdateTask = Task.Delay(UpdateInterval);
        UpdateTask.ContinueWith(
            (t) => Dispatcher.BeginInvoke(() => UpdateDirectoryList(!InProgress)), 
            CurrentCancellationToken.Token, 
            TaskContinuationOptions.OnlyOnRanToCompletion, 
            TaskScheduler.Default);
    }

    private void UpdateDirectoryList(bool finish)
    {
        if (finish || (currentFileQueue.Count >= MinUpdateThreshold))
        {
            for (int i = 0; finish || (i < DIR_LIST_UPDATE_THRESHOLD_MAX); i++)
            {
                FileStat fileStat;
                if (!currentFileQueue.TryDequeue(out fileStat))
                {
                    break;
                }

                FileClass item = FileClass.GenerateAndroidFile(fileStat);

                if (FileManipulator != null)
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
        catch (Exception e)
        {
            if ((e is not AggregateException) || ((e as AggregateException).InnerException is not TaskCanceledException))
            {
                throw;
            }
        }

        UpdateDirectoryList(true);
        InProgress = false;
        IsProgressVisible = false;
        ReadTask = null;
        CurrentCancellationToken = null;
    }
}
