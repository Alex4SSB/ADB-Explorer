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
            set
            {
                Set(ref copyPathEnabled, value);
                OnPropertyChanged(nameof(MoreEnabled));
            }
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
                OnPropertyChanged(nameof(MoreEnabled));
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

        private bool cutEnabled;
        public bool CutEnabled
        {
            get => cutEnabled;
            set => Set(ref cutEnabled, value);
        }

        private bool copyEnabled;
        public bool CopyEnabled
        {
            get => copyEnabled;
            set => Set(ref copyEnabled, value);
        }

        private bool pasteEnabled;
        public bool PasteEnabled
        {
            get => pasteEnabled;
            set => Set(ref pasteEnabled, value);
        }

        private bool renameEnabled;
        public bool RenameEnabled
        {
            get => renameEnabled;
            set => Set(ref renameEnabled, value);
        }

        private bool restoreEnabled;
        public bool RestoreEnabled
        {
            get => restoreEnabled;
            set => Set(ref restoreEnabled, value);
        }

        private bool deleteEnabled;
        public bool DeleteEnabled
        {
            get => deleteEnabled;
            set => Set(ref deleteEnabled, value);
        }

        private string deleteAction;
        public string DeleteAction
        {
            get => deleteAction;
            set
            {
                Set(ref deleteAction, value);
                OnPropertyChanged(nameof(MenuDeleteTooltip));
            }
        }

        private string restoreAction;
        public string RestoreAction
        {
            get => restoreAction;
            set
            {
                Set(ref restoreAction, value);
                OnPropertyChanged(nameof(MenuRestoreTooltip));
            }
        }

        private string copyPathAction;
        public string CopyPathAction
        {
            get => copyPathAction;
            set
            {
                Set(ref copyPathAction, value);
            }
        }

        public bool InstallUninstallEnabled => PackageActionsEnabled && InstallPackageEnabled;
        public bool CopyToTempEnabled => PackageActionsEnabled && !InstallPackageEnabled;
        public bool PushEnabled => PushFilesFoldersEnabled || PushPackageEnabled;
        public bool PushPackageVisible => PushPackageEnabled && Data.Settings.EnableApk;
        public bool MoreEnabled => PackageActionsEnabled || CopyPathEnabled;
        public string MenuDeleteTooltip => $"{DeleteAction} (Del)";
        public string MenuRestoreTooltip => $"{RestoreAction} (Ctrl+R)";


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
