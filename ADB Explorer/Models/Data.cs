using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Threading;

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

        public static Dictionary<string, string> CurrentPrettyNames { get; set; } = new();

        public static Dictionary<string, AbstractDevice.RootStatus> DevicesRoot { get; set; } = new();

        public static bool IsWin11 => Environment.OSVersion.Version > AdbExplorerConst.WIN11_VERSION;

        public static bool UseFluentStyles => IsWin11 || Settings.ForceFluentStyles;

        public static AppSettings Settings { get; set; } = new();

        public static ObservableList<FileClass> CutItems { get; private set; } = new();

        public static ObservableCollection<Log> CommandLog { get; set; } = new();

        public static ObservableList<FileClass> RecycledItems { get; set; } = new();

        public static void AddDummyRecycledItems(Dispatcher dispatcher)
        {
            RecycledItems.Clear();
            var countTask = Task.Run(() => CurrentADBDevice.CountRecycle());
            countTask.ContinueWith((t) =>
            {
                for (ulong i = 0; i < t.Result; i++)
                {
                    dispatcher.Invoke(() => RecycledItems.Add(new("", "", Converters.FileTypeClass.FileType.Unknown)));
                }
            });
        }
    }
}
