using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Models
{
    public class Log
    {
        public string Content { get; set; }

        public DateTime TimeStamp { get; set; }

        public Log(string content, DateTime? timeStamp = null)
        {
            Content = content;
            TimeStamp = timeStamp is null ? DateTime.Now : timeStamp.Value;
        }

        public override string ToString()
        {
            return $"{TimeStamp:HH:mm:ss:fff} ⁞ {Content}";
        }
    }

    public static class Data
    {
        public static ADBService.AdbDevice CurrentADBDevice { get; set; } = null;

        public static string CurrentPath { get; set; }
        public static string ParentPath { get; set; }

        public static Dictionary<Tuple<string, bool>, Icon> FileIcons { get; set; } = new();

        public static FileOperationQueue fileOperationQueue { get; set; }

        public static Dictionary<string, string> CurrentDisplayNames { get; set; } = new();

        public static AppSettings Settings { get; set; } = new();

        public static ObservableList<FileClass> CutItems { get; private set; } = new();

        public static ObservableCollection<Log> CommandLog { get; set; } = new();

        public static ObservableList<TrashIndexer> RecycleIndex { get; set; } = new();

        private static DateTime lastServerResponse = DateTime.Now;
        public static DateTime LastServerResponse
        {
            get => lastServerResponse;
            set
            {
                Set(ref lastServerResponse, value);
            }
        }

        public static ObservableList<Package> Packages { get; set; } = new();

        public static Version AppVersion => new(Properties.Resources.AppVersion);

        public static FileActionsEnable FileActions { get; set; } = new();

        public static DirectoryLister DirList { get; set; }

        public static string IsolatedStorageLocation { get; set; } = "";

        public static string ProgressRedirectionPath { get; set; } = "AdbProgressRedirection.exe";


        public static event PropertyChangedEventHandler PropertyChanged;
        private static bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }
        private static void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
    }
}
