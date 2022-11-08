using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ADB_Explorer.Models
{
    public abstract class UIDevice : AbstractDevice
    {
        public Device Device { get; protected set; }

        private bool deviceSelected;
        public bool DeviceSelected
        {
            get => deviceSelected;
            set => Set(ref deviceSelected, value);
        }

        public virtual string Tooltip { get; }

        public string TypeIcon => Device.Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            DeviceType.Service when Device is ServiceDevice service && service.MdnsType is ServiceDevice.ServiceType.QrCode => "\uED14",
            DeviceType.Service => "\uEDE4",
            DeviceType.Sideload => "\uED10",
            DeviceType.New => "\uE710",
            DeviceType.History => "\uE823",
            _ => throw new NotImplementedException(),
        };

        public string StatusIcon => Device.Status switch
        {
            DeviceStatus.Ok => "",
            DeviceStatus.Offline => "\uEBFF",
            DeviceStatus.Unauthorized => "\uEC00",
            _ => throw new NotImplementedException(),
        };

        public static implicit operator bool(UIDevice obj) => obj?.Device;

        protected UIDevice()
        {
            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        }

        private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppRuntimeSettings.CollapseDevices) && Data.RuntimeSettings.CollapseDevices)
            {
                DeviceSelected = false;
            }
        }
    }

    public class UILogicalDevice : UIDevice
    {
        private bool isOpen = false;
        public bool IsOpen
        {
            get => isOpen;
            private set => Set(ref isOpen, value);
        }

        public string Name => ((LogicalDevice)Device).Name;

        private byte? androidVersion = null;
        public byte? AndroidVersion
        {
            get => androidVersion;
            private set => Set(ref androidVersion, value);
        }

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
                    DeviceType.Service => "mDNS Service",
                    DeviceType.Sideload => "USB (Recovery)",
                    _ => throw new NotImplementedException(),
                };

                result += Device.Status switch
                {
                    DeviceStatus.Ok => "",
                    DeviceStatus.Offline => " - Offline",
                    DeviceStatus.Unauthorized => " - Unauthorized",
                    _ => throw new NotImplementedException(),
                };

                return result;
            }
        }

        public BrowseCommand BrowseCommand { get; private set; }
        public RemoveCommand RemoveCommand { get; private set; }
        public ToggleRootCommand ToggleRootCommand { get; private set; }
        public RebootCommand RebootCommand { get; private set; }
        public BootloaderCommand BootloaderCommand { get; private set; }
        public RecoveryCommand RecoveryCommand { get; private set; }
        public SideloadCommand SideloadCommand { get; private set; }
        public SideloadAutoCommand SideloadAutoCommand { get; private set; }

        public UILogicalDevice(LogicalDevice device)
        {
            Device = device;

            BrowseCommand = new(this);
            RemoveCommand = new(this);
            ToggleRootCommand = new(this);
            RebootCommand = new(this);
            BootloaderCommand = new(this);
            RecoveryCommand = new(this);
            SideloadCommand = new(this);
            SideloadAutoCommand = new(this);

            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        }

        private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppRuntimeSettings.UpdateCurrentDevice) && Data.RuntimeSettings.UpdateCurrentDevice)
            {
                if (Data.CurrentADBDevice is null || Data.CurrentADBDevice.ID != Device.ID)
                    IsOpen = false;
            }
        }

        public void SetOpen(List<UILogicalDevice> list, bool openState = true)
        {
            list.ForEach(device => device.IsOpen =
                device.Equals(this) && openState);
        }

        public static void SetOpen(List<UILogicalDevice> list)
        {
            list.ForEach((device) => device.IsOpen = false);
        }

        public void SetAndroidVersion(string version)
        {
            if (!IsOpen)
                return;

            if (byte.TryParse(version.Split('.')[0], out byte ver))
                AndroidVersion = ver;
        }
    }

    public class UIServiceDevice : UIDevice
    {
        public UIServiceDevice(ServiceDevice service)
        {
            Device = service;

            PairCommand = new(this);
        }

        public PairCommand PairCommand { get; private set; }

        private string uiPairingCode;
        public string UIPairingCode
        {
            get => uiPairingCode;
            set
            {
                if (Set(ref uiPairingCode, value))
                    ((ServiceDevice)Device).PairingCode = uiPairingCode?.Replace("-", "");
            }
        }

        public override string Tooltip => $"mDNS Service - {(((ServiceDevice)Device).MdnsType is ServiceDevice.ServiceType.QrCode ? "QR Pairing" : "Ready To Pair")}";
    }

    public class UINewDevice : UIDevice
    {
        public ConnectCommand ConnectCommand { get; private set; }

        public ClearCommand ClearCommand { get; private set; }

        private bool isPairingEnabled = false;
        public bool IsPairingEnabled
        {
            get => isPairingEnabled;
            set => Set(ref isPairingEnabled, value);
        }

        private string uiPairingCode;
        public string UIPairingCode
        {
            get => uiPairingCode;
            set
            {
                if (Set(ref uiPairingCode, value))
                    ((NewDevice)Device).PairingCode = uiPairingCode?.Replace("-", "");
            }
        }

        public UINewDevice()
        {
            Device = new NewDevice();

            ConnectCommand = new(this);
            ClearCommand = new(this);
        }

        public void ClearDevice()
        {
            var dev = Device as NewDevice;

            dev.IpAddress =
            dev.ConnectPort =
            dev.PairingPort =
            UIPairingCode = "";
            IsPairingEnabled = false;
        }

        public void EnablePairing()
        {
            var dev = Device as NewDevice;

            dev.PairingPort =
            UIPairingCode = "";
            IsPairingEnabled = true;
        }
    }

    public class UIHistoryDevice : UIDevice
    {
        public override string Tooltip => "Saved Device";

        public UIHistoryDevice(HistoryDevice device)
        {
            Device = device;

            ConnectCommand = new(this);
            RemoveCommand = new(this);
        }

        public ConnectCommand ConnectCommand { get; private set; }
        public RemoveCommand RemoveCommand { get; private set; }
    }
}
