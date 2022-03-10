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

        private Dispatcher Dispatcher { get; }
        private DispatcherTimer Timer { get; }
        private Task CurrentTask { get; set; }
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
            Timer = new(DIR_LIST_UPDATE_INTERVAL, 
                        DispatcherPriority.Normal, 
                        (s, e) => UpdateDirectoryList(), 
                        dispatcher);
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
                CurrentPath = path;
            }).Wait();

            CurrentCancellationToken = new CancellationTokenSource();
            currentFileQueue = new ConcurrentQueue<FileStat>();
            CurrentTask = Task.Run(() => Device.ListDirectory(CurrentPath, ref currentFileQueue, CurrentCancellationToken.Token), CurrentCancellationToken.Token);
            CurrentTask.ContinueWith((t) => Dispatcher.BeginInvoke(StopDirectoryList), CurrentCancellationToken.Token);

            Timer.Start();
        }

        private void UpdateDirectoryList()
        {
            if (CurrentTask == null)
            {
                return;
            }

            var newFiles = currentFileQueue.DequeueAllExisting().Select(f => FileClass.GenerateAndroidFile(f)).ToArray();
            FileList.AddRange(newFiles);
        }

        private void StopDirectoryList()
        {
            if (CurrentTask == null)
            {
                return;
            }

            Timer.Stop();
            CurrentCancellationToken.Cancel();

            try
            {
                CurrentTask.Wait();
            }
            catch (Exception e)
            {
                if ((e is not AggregateException) || ((e as AggregateException).InnerException is not TaskCanceledException))
                {
                    throw;
                }
            }

            UpdateDirectoryList();
            CurrentTask = null;
            CurrentCancellationToken = null;
            InProgress = false;
        }
    }
}
