using ADB_Explorer.Helpers;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services
{
    public static class UISettings
    {
        public static ObservableList<SettingsGroup> SettingsList { get; set; }

        public static IEnumerable<AbstractSetting> SortSettings => SettingsList.SelectMany(group => group.Children).OrderBy(sett => sett.Description);

        public static void Init()
        {
            Type appSettings = Settings.GetType();
            ResetCommand resetCommand = new();
            ShowAnimationTipCommand tipCommand = new();
            ChangeDefaultPathCommand defPathCommand = new();
            ChangeAdbPathCommand adbPathCommand = new();

            SettingsList = new()
            {
                new SettingsGroup("ADB", new()
                {
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableMdns)), "Enable mDNS (experimental)", "ADB"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDevices)), "Poll For Devices", "ADB"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.PollBattery)), "Poll For Battery Status", "ADB"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableLog)), "Enable Command Log", "ADB"),
                }),
                new SettingsGroup("Device", new()
                {
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoRoot)), "Automatically Enable Root", "Device"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.RememberIp)), "Remember Last IP", "Device"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.RememberPort)), "Remember Last Port", "Device", enableProp: appSettings.GetProperty(nameof(Settings.RememberIp))),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoOpen)), "Automatically Open For Browsing", "Device"),
                }),
                new SettingsGroup("Drives & Features", new()
                {
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDrives)), "Poll For Drives", "Drives & Features"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableRecycle)), "Enable Recycle Bin", "Drives & Features"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableApk)), "Enable APK Handling", "Drives & Features"),
                }),
                new SettingsGroup("File Behavior", new()
                {
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowExtensions)), "Show File Name Extensions", "File Behavior"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowHiddenItems)), "Show Hidden Items", "File Behavior"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowSystemPackages)), "Show System Apps", "File Behavior", visibleProp: appSettings.GetProperty(nameof(Settings.EnableApk))),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.PullOnDoubleClick)), "Pull To Default Folder On Double-Click", "File Behavior", enableProp: appSettings.GetProperty(nameof(Settings.EnableDoubleClickPull))),
                }),
                new SettingsGroup("Working Directories", new()
                {
                    new StringSetting(appSettings.GetProperty(nameof(Settings.DefaultFolder)), "Default Folder", defPathCommand, "Working Directories"),
                    new StringSetting(appSettings.GetProperty(nameof(Settings.ManualAdbPath)), "Override ADB Path", adbPathCommand, "Working Directories", commands: resetCommand),
                }),
                //new EnumGroup("Theme", new()
                //{
                //    //new Setting<Models.AppTheme>(appSettings.GetProperty(nameof(Settings.Theme)), ""),
                //}),
                new SettingsGroup("Graphics", new()
                {
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.ForceFluentStyles)), "Force Fluent Styles", "Graphics", visibleProp: appSettings.GetProperty(nameof(Settings.HideForceFluent))),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.SwRender)), "Disable Hardware Acceleration", "Graphics"),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.DisableAnimation)), "Disable Animations", "Graphics", null, null, resetCommand, tipCommand),
                    new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableSplash)), "Display Splash Screen", "Graphics"),
                }),
            };
        }
    }

    public class SettingsGroup : INotifyPropertyChanged
    {
        public string Name { get; set; }

        public List<AbstractSetting> Children { get; set; }

        public SettingsGroup(string name, List<AbstractSetting> children)
        {
            Name = name;
            Children = children;    
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    //public class BoolGroup : SettingsGroup
    //{
    //    public List<AbstractSetting<bool>> Children { get; set; }

    //    public BoolGroup(string name, List<AbstractSetting<bool>> children) : base(name)
    //    {
    //        Children = children;
    //    }
    //}

    //public class StringGroup : SettingsGroup
    //{
    //    public List<AbstractSetting<string>> Children { get; set; }

    //    public StringGroup(string name, List<AbstractSetting<string>> children) : base(name)
    //    {
    //        Children = children;
    //    }
    //}

    //public class EnumGroup : SettingsGroup
    //{
    //    public List<AbstractSetting<Enum>> Children { get; set; }

    //    public EnumGroup(string name, List<AbstractSetting<Enum>> children) : base(name)
    //    {
    //        Children = children;
    //    }
    //}

    public abstract class AbstractSetting : INotifyPropertyChanged
    {
        protected readonly PropertyInfo valueProp;
        protected readonly PropertyInfo enableProp;
        protected readonly PropertyInfo visibleProp;

        public string Description { get; private set; }
        public string GroupName { get; private set; }
        public SettingButton[] Commands { get; private set; }

        public bool IsEnabled => enableProp is null || (bool)enableProp.GetValue(Settings);
        public Visibility Visibility => visibleProp is null || (bool)visibleProp.GetValue(Settings) ? Visibility.Visible : Visibility.Collapsed;

        protected AbstractSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        {
            this.enableProp = enableProp;
            this.visibleProp = visibleProp;
            this.valueProp = valueProp;
            Description = description;
            GroupName = groupName;
            Commands = commands;

            Settings.PropertyChanged += Settings_PropertyChanged;
        }

        protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == enableProp?.Name)
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
            else if (e.PropertyName == visibleProp?.Name)
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class BoolSetting : AbstractSetting
    {
        public bool Value
        {
            get => (bool)valueProp.GetValue(Settings);
            set => valueProp.SetValue(Settings, value);
        }

        public BoolSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
            : base(valueProp, description, groupName, enableProp, visibleProp, commands)
        { }

        protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == valueProp.Name)
            {
                OnPropertyChanged(nameof(Value));
            }
            else if (e.PropertyName == enableProp?.Name)
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
            else if (e.PropertyName == visibleProp?.Name)
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public class StringSetting : AbstractSetting
    {
        public SettingButton SetButton { get; set; }

        public string Value
        {
            get => (string)valueProp.GetValue(Settings);
            set => valueProp.SetValue(Settings, value);
        }

        public StringSetting(PropertyInfo valueProp, string description, SettingButton setButton, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
            : base(valueProp, description, groupName, enableProp, visibleProp, commands)
        {
            SetButton = setButton;
        }

        protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == valueProp.Name)
            {
                OnPropertyChanged(nameof(Value));
            }
            else if (e.PropertyName == enableProp?.Name)
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
            else if (e.PropertyName == visibleProp?.Name)
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    //public class EnumSetting : AbstractSetting
    //{
    //    public Enum Value
    //    {
    //        get => (Enum)valueProp.GetValue(Settings);
    //        set => valueProp.SetValue(Settings, value);
    //    }

    //    public EnumSetting(PropertyInfo valueProp, string description, string groupName = null, bool showResetButton = false, PropertyInfo enableProp = null, PropertyInfo visibleProp = null)
    //        : base(valueProp, description, groupName, showResetButton, enableProp, visibleProp)
    //    { }
    //}

    public abstract class SettingButton
    {
        public virtual bool IsEnabled { get; } = true;

    }

    public class ResetCommand : SettingButton
    {
        public static void Action()
        {
            Process.Start(Environment.ProcessPath);
            Application.Current.Shutdown();
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class ShowAnimationTipCommand : SettingButton
    {
        public static void Action()
        {
            DialogService.ShowMessage("The app has many animations that are enabled as part of the fluent design.\nThe side views animation is always disabled when the app window is maximized on a secondary display.\n\n• Checking this setting disables all app animations except progress bars, progress rings, and drive usage bars.", "App Animations", DialogService.DialogIcon.Tip);
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class ChangeDefaultPathCommand : SettingButton
    {
        public static void Action()
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false
            };
            if (Settings.DefaultFolder != "")
                dialog.DefaultDirectory = Settings.DefaultFolder;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Settings.DefaultFolder = dialog.FileName;
            }
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class ChangeAdbPathCommand : SettingButton
    {
        public static void Action()
        {
            var dialog = new OpenFileDialog()
            {
                Multiselect = false,
                Title = "Select ADB Executable",
                Filter = "ADB Executable|adb.exe",
            };

            if (!string.IsNullOrEmpty(Settings.ManualAdbPath))
            {
                try
                {
                    dialog.InitialDirectory = Directory.GetParent(Settings.ManualAdbPath).FullName;
                }
                catch (Exception) { }
            }

            if (dialog.ShowDialog() == true)
            {
                string message = "";
                var version = ADBService.VerifyAdbVersion(dialog.FileName);
                if (version is null)
                {
                    message = "Could not get ADB version from provided path.";
                }
                else if (version < MIN_ADB_VERSION)
                {
                    message = "ADB version from provided path is too low.";
                }

                if (message != "")
                {
                    //SettingsSplitView.IsPaneOpen = false;
                    DialogService.ShowMessage(message, "Fail to override ADB", DialogService.DialogIcon.Exclamation);
                    return;
                }

                Settings.ManualAdbPath = dialog.FileName;
            }
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }
}
