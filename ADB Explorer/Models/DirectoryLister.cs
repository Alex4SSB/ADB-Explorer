using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models
{
    public class DirectoryLister : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ADBService.AdbDevice Device { get; }
        public ObservableList<FileClass> FileList { get; }

        private string currentPath;
        public string CurrentPath
        {
            get => currentPath;
            private set => SetField(ref currentPath, value);
        }

        private bool inProgress;
        public bool InProgress
        { 
            get => inProgress;
            private set => SetField(ref inProgress, value);
        }

        private bool isProgressVisible;
        public bool IsProgressVisible
        {
            get => isProgressVisible;
            private set => SetField(ref isProgressVisible, value);
        }

        private Dispatcher Dispatcher { get; }
        private Task UpdateTask { get; set; }
        private TimeSpan UpdateInterval { get; set; }
        private int MinUpdateThreshold { get; set; }
        private Task ReadTask { get; set; }
        private CancellationTokenSource CurrentCancellationToken { get; set; }
        private ConcurrentQueue<FileStat> currentFileQueue;

        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public DirectoryLister(Dispatcher dispatcher, ADBService.AdbDevice adbDevice)
        {
            Dispatcher = dispatcher;
            FileList = new();
            Device = adbDevice;
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
            
            Task.Delay(DIR_LIST_VISIBLE_PROGRESS_DELAY).ContinueWith((t) => Dispatcher.BeginInvoke(() => { IsProgressVisible = InProgress; }), CurrentCancellationToken.Token);

            ScheduleUpdate();
        }

        private void ScheduleUpdate()
        {
            if (ReadTask == null)
            {
                return;
            }

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
                (t) => Dispatcher.BeginInvoke(UpdateDirectoryList), 
                CurrentCancellationToken.Token, 
                TaskContinuationOptions.OnlyOnRanToCompletion, 
                TaskScheduler.Default);
        }

        private void UpdateDirectoryList()
        {
            var updateCutItems = Data.CutItems.Any() && Data.CutItems[0].ParentPath == CurrentPath;

            if ((ReadTask == null) || (currentFileQueue.Count >= MinUpdateThreshold))
            {
                for (int i = 0; (!InProgress) || (i < DIR_LIST_UPDATE_THRESHOLD_MAX); i++)
                {
                    FileStat fileStat;
                    if (!currentFileQueue.TryDequeue(out fileStat))
                    {
                        break;
                    }

                    FileClass item = FileClass.GenerateAndroidFile(fileStat);
                    if (updateCutItems)
                    {
                        var cutItem = Data.CutItems.Where(f => f.FullPath == fileStat.FullPath);
                        if (cutItem.Any())
                        {
                            item.IsCut = true;
                            Data.CutItems.Remove(cutItem.First());
                            Data.CutItems.Add(item);
                        }
                    }

                    if (CurrentPath == RECYCLE_PATH)
                    {
                        var query = Data.RecycleIndex.Where(index => index.RecycleName == item.FullName);
                        if (query.Any())
                            item.TrashIndex = query.First();
                    }

                    FileList.Add(item);
                }
            }

            ScheduleUpdate();
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

            ReadTask = null;
            CurrentCancellationToken = null;
            InProgress = false;
            IsProgressVisible = false;
            UpdateDirectoryList();
        }
    }
}
