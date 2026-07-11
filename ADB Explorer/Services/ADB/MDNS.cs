using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class MDNS : ViewModelBase
{
    public MDNS()
    {
        state = MdnsState.Disabled;
    }

    public enum MdnsState
    {
        Disabled,
        InProgress,
        NotRunning,
        Running,
    }

    private MdnsState state;
    public MdnsState State
    {
        get => state;
        private set
        {
            if (Set(ref state, value))
            {
                if (value is MdnsState.InProgress)
                    checkStart = DateTime.Now;
                else
                    Progress = 0.0;
            }
        }
    }

    private double progress;
    public double Progress
    {
        get => progress;
        private set
        {
            if (Set(ref progress, value))
                OnPropertyChanged(nameof(TimePassedString));
        }
    }

    private DateTime checkStart;

    private TimeSpan timePassed = TimeSpan.MinValue;

    public string TimePassedString => timePassed == TimeSpan.MinValue ? "" : Converters.UnitConverter.ToTime(timePassed.TotalSeconds, useMilli: false, digits: 0);

    private void UpdateProgress()
    {
        timePassed = DateTime.Now.Subtract(checkStart);

        Progress = timePassed < AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME
            ? timePassed / AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME * 100
            : 100;
    }

    private PairingQrClass? qrClass;
    public PairingQrClass? QrClass
    {
        get => qrClass;
        private set => Set(ref qrClass, value);
    }

    public void Enable()
    {
        if (State is not MdnsState.Disabled)
            return;

        State = MdnsState.InProgress;
        QrClass = new();
        Check();
    }

    public void Disable()
    {
        QrClass = null;
        State = MdnsState.Disabled;
    }

    public void Restart()
    {
        ADBService.KillAdbServer();
        if (AdbHelper.CurrentAdbState.Status is not AdbHelper.AdbStatus.Valid)
            return;

        State = MdnsState.Disabled;
        Enable();
    }

    private void Check()
    {
        Task.Run(() =>
        {
            MdnsState newState = ProbeMdnsState();

            // Settings want mDNS, but the server was started without it (or with a backend that failed).
            // Restart ADB once so the next server inherits ADB_MDNS_OPENSCREEN, instead of showing "down".
            if (newState is MdnsState.NotRunning && Data.Settings.EnableMdns)
            {
                try
                {
                    ADBService.IsMdnsEnabled = true;
                    ADBService.KillAdbServer(restart: true);
                    newState = ProbeMdnsState();
                }
                catch
                {
                    newState = MdnsState.NotRunning;
                }
            }

            App.SafeInvoke(() => State = newState);
        });
        Task.Run(async () =>
        {
            while (State is MdnsState.InProgress)
            {
                App.SafeInvoke(UpdateProgress);
                await Task.Delay(AdbExplorerConst.MDNS_STATUS_UPDATE_INTERVAL);
            }
        });
    }

    private static MdnsState ProbeMdnsState()
    {
        try
        {
            return ADBService.CheckMDNS() ? MdnsState.Running : MdnsState.NotRunning;
        }
        catch
        {
            return MdnsState.NotRunning;
        }
    }

    public class PairingQrClass
    {
        public string ServiceName { get; }
        public string Password { get; }
        public SolidColorBrush Background { get; }
        public SolidColorBrush Foreground { get; }

        public DrawingImage Image => string.IsNullOrEmpty(PairingString) ? null : QrGenerator.GenerateQR(PairingString, Background, Foreground);
        public string PairingString => WiFiPairingService.CreatePairingString(ServiceName, Password);

        public PairingQrClass()
        {
            ServiceName = AdbExplorerConst.PAIRING_SERVICE_PREFIX + RandomString.GetUniqueKey(10);
            Password = RandomString.GetUniqueKey(12);

            Background = (SolidColorBrush)App.Current.FindResource("QrBackgroundBrush");
            Foreground = (SolidColorBrush)App.Current.FindResource("QrForegroundBrush");
        }
    }
}
