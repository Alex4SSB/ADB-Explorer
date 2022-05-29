using ADB_Explorer.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Services
{
    public class AppSettings : INotifyPropertyChanged
    {
        #region paths

        private string defaultFolder;
        /// <summary>
        /// Default Folder For Pull On Double-Click And Folder / File Browsers
        /// </summary>
        public string DefaultFolder
        {
            get => Get(ref defaultFolder, "");
            set => Set(ref defaultFolder, value);
        }

        private string manualAdbPath;
        public string ManualAdbPath
        {
            get => Get(ref manualAdbPath, "");
            set => Set(ref manualAdbPath, value);
        }

        #endregion

        #region explorer

        private bool pullOnDoubleClick;
        public bool PullOnDoubleClick
        {
            get => Get(ref pullOnDoubleClick, false);
            set => Set(ref pullOnDoubleClick, value);
        }

        private bool showExtensions;
        public bool ShowExtensions
        {
            get => Get(ref showExtensions, true);
            set => Set(ref showExtensions, value);
        }

        private bool showHiddenItems;
        public bool ShowHiddenItems
        {
            get => Get(ref showHiddenItems, true);
            set => Set(ref showHiddenItems, value);
        }

        #endregion

        #region device

        private bool rememberIp;
        public bool RememberIp
        {
            get => Get(ref rememberIp, false);
            set => Set(ref rememberIp, value);
        }

        private string lastIp;
        public string LastIp
        {
            get => Get(ref lastIp, "");
            set => Set(ref lastIp, value);
        }

        private bool rememberPort;
        public bool RememberPort
        {
            get => Get(ref rememberPort, false);
            set => Set(ref rememberPort, value);
        }

        private string lastPort;
        public string LastPort
        {
            get => Get(ref lastPort, "");
            set => Set(ref lastPort, value);
        }

        private bool autoOpen;
        /// <summary>
        /// Automatically Open Device For Browsing
        /// </summary>
        public bool AutoOpen
        {
            get => Get(ref autoOpen, false);
            set => Set(ref autoOpen, value);
        }

        private bool autoRoot;
        /// <summary>
        /// Automatically Try To Open Devices Using Root Privileges
        /// </summary>
        public bool AutoRoot
        {
            get => Get(ref autoRoot, false);
            set => Set(ref autoRoot, value);
        }

        private bool enableRecycle;
        /// <summary>
        /// Enable moving deleted items to a special folder, instead of permanently deleting them
        /// </summary>
        public bool EnableRecycle
        {
            get => Get(ref enableRecycle, false);
            set => Set(ref enableRecycle, value);
        }

        #endregion

        #region adb

        private bool enableMdns;
        /// <summary>
        /// Runs ADB server with mDNS enabled, polling for services is enabled in the connection expander
        /// </summary>
        public bool EnableMdns
        {
            get => Get(ref enableMdns, false);
            set => Set(ref enableMdns, value);
        }

        private bool pollDevices;
        /// <summary>
        /// <see langword="false"/> - disables polling for both ADB devices and mDNS services. Enables manual refresh button
        /// </summary>
        public bool PollDevices
        {
            get => Get(ref pollDevices, true);
            set => Set(ref pollDevices, value);
        }

        private bool pollBattery;
        /// <summary>
        /// Enables battery information for all devices (polling and displaying)
        /// </summary>
        public bool PollBattery
        {
            get => Get(ref pollBattery, true);
            set => Set(ref pollBattery, value);
        }

        private bool enableLog;
        /// <summary>
        /// <see langword="false"/> - disables logging of commands and command log button
        /// </summary>
        public bool EnableLog
        {
            get => Get(ref enableLog, false);
            set => Set(ref enableLog, value);
        }

        #endregion

        private bool showExtendedView;
        /// <summary>
        /// File Operations Detailed View
        /// </summary>
        public bool ShowExtendedView
        {
            get => true; // Get(ref showExtendedView, true);
            //set => Set(ref showExtendedView, value);
        }

        #region about

        private bool checkForUpdates;
        /// <summary>
        /// GET releases on GitHub repo on each launch
        /// </summary>
        public bool CheckForUpdates
        {
            get => Get(ref checkForUpdates, true);
            set => Set(ref checkForUpdates, value);
        }

        #endregion

        #region theme

        private bool forceFluentStyles;
        /// <summary>
        /// Use Fluent [Windows 11] Styles In Windows 10
        /// </summary>
        public bool ForceFluentStyles
        {
            get => Get(ref forceFluentStyles, false);
            set => Set(ref forceFluentStyles, value);
        }

        private AppTheme appTheme;
        public AppTheme Theme
        {
            get => Get(ref appTheme, AppTheme.windowsDefault);
            set => Set(ref appTheme, value);
        }

        #endregion theme

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null, bool saveToDisk = true)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            if (saveToDisk)
            {
                Storage.StoreValue(propertyName, value);
            }

            OnPropertyChanged(propertyName);
        }
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected virtual T Get<T>(ref T storage, T defaultValue, [CallerMemberName] string propertyName = null)
        {
            if (storage is null || storage.Equals(default(T)))
            {
                var value = storage switch
                {
                    Enum => Storage.RetrieveEnum<T>(propertyName),
                    bool => Storage.RetrieveBool(propertyName),
                    string => Storage.RetrieveValue(propertyName),
                    null when typeof(T) == typeof(string) => Storage.RetrieveValue(propertyName),
                    _ => throw new NotImplementedException(),
                };

                storage = (T)(value is null ? defaultValue : value);
            }

            return storage;
        }
    }
}
