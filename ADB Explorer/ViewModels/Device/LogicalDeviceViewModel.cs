﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class LogicalDeviceViewModel : DeviceViewModel
{
    #region Full properties

    private LogicalDevice device;
    protected new LogicalDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    private bool isOpen;
    /// <summary>
    /// Device is open for browsing
    /// </summary>
    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (Set(ref isOpen, value) && Root is RootStatus.Enabled)
            {
                if (!value && Data.Settings.UnrootOnDisconnect is true)
                    ADBService.Unroot(Device);

                if (value)
                    Data.RuntimeSettings.IsRootActive = true;
            }
        }
    }

    private byte? androidVersion;
    public byte? AndroidVersion
    {
        get => androidVersion;
        private set => Set(ref androidVersion, value);
    }

    private bool useIdForName;
    public bool UseIdForName
    {
        get => useIdForName;
        set => Set(ref useIdForName, value);
    }

    #endregion

    #region Read only properties

    public string Name
    {
        get
        {
            // to prevent displaying [Service] for offline connect services acquired via adb devices -l upon first contact
            if (string.IsNullOrEmpty(Device.Name) && DiscoverTime - DateTime.Now < AdbExplorerConst.SERVICE_DISPLAY_DELAY)
                return " ";

            return UseIdForName ? Device.ID : Device.Name;
        }
    }

    public string BaseID => Type is DeviceType.Service ? ID.Split('.')[0] : ID;

    public string LogicalID => Type is DeviceType.Service && ID.Count(c => c is '-') > 1 ? ID.Split('-')[1] : ID;

    public RootStatus Root => Device.Root;

    public bool AndroidVersionIncompatible => AndroidVersion is not null && AndroidVersion < AdbExplorerConst.MIN_SUPPORTED_ANDROID_VER;

    public override string Tooltip
    {
        get
        {
            string result = "";

            result += Device.Type switch
            {
                DeviceType.Local => "USB",
                DeviceType.Remote => "WiFi",
                DeviceType.Emulator => "Emulator",
                DeviceType.WSA => "WSA",
                DeviceType.Service => "mDNS Service",
                DeviceType.Recovery => "USB (Recovery)",
                DeviceType.Sideload => "USB (Sideload)",
                _ => throw new NotSupportedException(),
            };

            result += Device.Status switch
            {
                DeviceStatus.Ok => "",
                DeviceStatus.Offline => " - Offline",
                DeviceStatus.Unauthorized => " - Unauthorized",
                _ => throw new NotSupportedException(),
            };

            return result;
        }
    }

    public Battery Battery => Device.Battery;

    public ObservableList<DriveViewModel> Drives => Device.Drives;

    #endregion

    public DateTime DiscoverTime { get; }

    #region Commands

    public DeviceAction BrowseCommand { get; }
    public DeviceAction RemoveCommand { get; }
    public DeviceAction ToggleRootCommand { get; }
    public List<object> RebootCommands { get; } = new();

    #endregion

    public LogicalDeviceViewModel(LogicalDevice device) : base(device)
    {
        DiscoverTime = DateTime.Now;

        Device = device;
        if (Device.Type is DeviceType.Emulator)
            UseIdForName = true;

        BrowseCommand = new(() => !IsOpen && device.Status is DeviceStatus.Ok && device.Type is not DeviceType.Sideload,
                            () => DeviceHelper.BrowseDeviceAction(this));

        RemoveCommand = DeviceHelper.RemoveDeviceCommand(this);

        ToggleRootCommand = DeviceHelper.ToggleRootDeviceCommand(this);

        foreach (RebootCommand.RebootType item in Enum.GetValues(typeof(RebootCommand.RebootType)))
        {
            RebootCommands.Add(new RebootCommand(this, item));

            if (item is RebootCommand.RebootType.Title)
                RebootCommands.Add(new Separator() { Margin = new(-11, 0, -11, 0)});
        }

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.DeviceToOpen))
        {
            IsOpen = Data.RuntimeSettings.DeviceToOpen is not null
                && Data.RuntimeSettings.DeviceToOpen.ID == ID;
        }
    }

    #region Setter functions

    public void SetAndroidVersion(string version)
    {
        if (!IsOpen)
            return;

        if (byte.TryParse(version.Split('.')[0], out byte ver))
            AndroidVersion = ver;
    }

    public void UpdateDevice(LogicalDeviceViewModel other) => UpdateDevice(other.Device);

    public void UpdateDevice(LogicalDevice other)
    {
        Device.Name = other.Name;
        SetStatus(other.Status);
    }

    public void EnableRoot(bool enable)
    {
        Device.EnableRoot(enable);
        OnPropertyChanged(nameof(Root));
    }

    public bool SetRootStatus(RootStatus status)
    {
        if (Root != status)
        {
            Device.Root = status;
            OnPropertyChanged(nameof(Root));

            if (IsOpen)
                Data.RuntimeSettings.IsRootActive = status is RootStatus.Enabled;

            return true;
        }

        return false;
    }

    public void UpdateBattery() => Device.UpdateBattery();

    public Task<bool> UpdateDrives(IEnumerable<Drive> drives, Dispatcher dispatcher, bool asyncClassify = false) => Device.UpdateDrives(drives, dispatcher, asyncClassify);

    public Task<bool> UpdateDrives(LogicalDeviceViewModel other, Dispatcher dispatcher, bool asyncClassify = false)
        => UpdateDrives(other.Device.Drives.Select(d => d.Drive), dispatcher, asyncClassify);

    #endregion
}

public class LogicalDeviceViewModelEqualityComparer : IEqualityComparer<LogicalDeviceViewModel>
{
    public bool Equals(LogicalDeviceViewModel x, LogicalDeviceViewModel y)
    {
        return x.ID == y.ID && x.Status == y.Status;
    }

    public int GetHashCode([DisallowNull] LogicalDeviceViewModel obj)
    {
        return (obj.ID.GetHashCode() + obj.Status.GetHashCode()).GetHashCode();
    }
}
