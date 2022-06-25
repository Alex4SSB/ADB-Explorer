using ADB_Explorer.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Models
{
    public class FileActionsEnable : INotifyPropertyChanged
    {
        private bool newMenuVisible;
        public bool NewMenuVisible
        {
            get => newMenuVisible;
            set => Set(ref newMenuVisible, value);
        }

        private bool pushFilesFoldersEnabled;
        public bool PushFilesFoldersEnabled
        {
            get => pushFilesFoldersEnabled;
            set
            {
                Set(ref pushFilesFoldersEnabled, value);
                OnPropertyChanged(nameof(PushEnabled));
            }
        }

        private bool pushPackageEnabled;
        public bool PushPackageEnabled
        {
            get => pushPackageEnabled;
            set {
                Set(ref pushPackageEnabled, value);
                OnPropertyChanged(nameof(PushEnabled));
                OnPropertyChanged(nameof(PushPackageVisible));
            }
        }

        private bool copyPathEnabled;
        public bool CopyPathEnabled
        {
            get => copyPathEnabled;
            set => Set(ref copyPathEnabled, value);
        }

        private bool packageActionsEnabled;
        public bool PackageActionsEnabled
        {
            get => packageActionsEnabled;
            set
            {
                Set(ref packageActionsEnabled, value);
                OnPropertyChanged(nameof(InstallUninstallEnabled));
                OnPropertyChanged(nameof(CopyToTempEnabled));
            }
        }

        private bool installPackageEnabled;
        public bool InstallPackageEnabled
        {
            get => installPackageEnabled;
            set
            {
                Set(ref installPackageEnabled, value);
                OnPropertyChanged(nameof(InstallUninstallEnabled));
                OnPropertyChanged(nameof(CopyToTempEnabled));
            }
        }

        private bool uninstallPackageEnabled;
        public bool UninstallPackageEnabled
        {
            get => uninstallPackageEnabled;
            set => Set(ref uninstallPackageEnabled, value);
        }

        private bool uninstallVisible;
        public bool UninstallVisible
        {
            get => uninstallVisible;
            set => Set(ref uninstallVisible, value);
        }

        public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
        public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
        public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
        public bool PushPackageVisible => PushPackageEnabled && Data.Settings.EnableApk;


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
