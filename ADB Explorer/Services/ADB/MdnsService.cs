namespace ADB_Explorer.Services;

public class MdnsService : ObservableObject
{
    public MdnsService()
    {
        _state = MdnsState.Disabled;
    }

    public enum MdnsState
    {
        Disabled,
        InProgress,
        NotRunning,
        Running,
    }

    private MdnsState _state;
    public MdnsState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                if (value is MdnsState.InProgress)
                    _checkStart = DateTime.Now;
                else
                    Progress = 0.0;
            }
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
                OnPropertyChanged(nameof(TimePassedString));
        }
    }

    private DateTime _checkStart;

    private TimeSpan _timePassed = TimeSpan.MinValue;

    public string TimePassedString => _timePassed == TimeSpan.MinValue ? "" : UnitFormatter.ToTime(_timePassed.TotalSeconds, useMilli: false, digits: 0);

    private void UpdateProgress()
    {
        // The probe task and this progress loop run concurrently. Once the state has
        // transitioned out of InProgress (which resets Progress to 0), a stale update
        // dispatched from the loop must not run - otherwise it would freeze the bar at
        // the last time-based value instead of clearing it.
        if (State is not MdnsState.InProgress)
            return;

        _timePassed = DateTime.Now.Subtract(_checkStart);

        Progress = _timePassed < AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME
            ? _timePassed / AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME * 100
            : 100;
    }

    private PairingQrClass? _qrClass;
    public PairingQrClass? QrClass
    {
        get => _qrClass;
        private set => SetProperty(ref _qrClass, value);
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
        AdbService.KillAdbServer();
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
                    AdbService.IsMdnsEnabled = true;
                    AdbService.KillAdbServer(restart: true);
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
            return AdbService.CheckMDNS() ? MdnsState.Running : MdnsState.NotRunning;
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
