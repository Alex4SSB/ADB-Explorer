using ADB_Explorer.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Models
{
    public class FileActionsEnable : INotifyPropertyChanged
    {
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
            set => Set(ref packageActionsEnabled, value);
        }

        private bool installPackageEnabled;
        public bool InstallPackageEnabled
        {
            get => installPackageEnabled;
            set => Set(ref installPackageEnabled, value);
        }

        public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
        public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;

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
