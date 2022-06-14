using Microsoft.Win32;
using ModernWpf;
using System;
using System.ComponentModel;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;

namespace ADB_Explorer.Services
{
    internal class ThemeService : INotifyPropertyChanged
    {
        //https://medium.com/southworks/handling-dark-light-modes-in-wpf-3f89c8a4f2db

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";
        private const string QueryPrefix = "SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath";

        private ApplicationTheme? windowsTheme = null;
        public ApplicationTheme WindowsTheme
        {
            get
            {
                if (windowsTheme is null)
                    WatchTheme();

                return windowsTheme.Value;
            }
            set
            {
                windowsTheme = value;
                NotifyPropertyChanged();
            }
        }

        public void WatchTheme()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            string query = $@"{QueryPrefix} = '{currentUser.User.Value}\\{RegistryKeyPath.Replace(@"\", @"\\")}' AND ValueName = '{RegistryValueName}'";

            // This can fail on Windows 7, but we do not support
            ManagementEventWatcher watcher = new(query);
            watcher.EventArrived += (sender, args) =>
            {
                WindowsTheme = GetWindowsTheme();
            };

            watcher.Start();

            WindowsTheme = GetWindowsTheme();
        }

        private static ApplicationTheme GetWindowsTheme()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return (key?.GetValue(RegistryValueName)) is int and < 1 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal static class MonitorInfo
    {
        private enum MonitorType
        {
            Primary = 0x00000001,
            Nearest = 0x00000002,
        }

        private static IntPtr? handler = null;
        private static IntPtr primaryMonitor => MonitorFromWindow(IntPtr.Zero, (Int32)MonitorType.Primary);


        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, Int32 flags);

        public static void Init(Window window)
        {
            if (handler is null)
                handler = new WindowInteropHelper(window).EnsureHandle();
        }

        public static bool? IsPrimaryMonitor(Window window)
        {
            Init(window);
            return IsPrimaryMonitor();
        }

        public static bool? IsPrimaryMonitor()
        {
            if (handler is null)
                return null;

            var current = MonitorFromWindow(handler.Value, (Int32)MonitorType.Nearest);

            return current == primaryMonitor;
        }
    }
}
