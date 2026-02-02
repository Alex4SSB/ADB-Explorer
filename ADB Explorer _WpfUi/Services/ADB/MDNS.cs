using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class MDNS : ViewModelBase
{
    public MDNS()
    {
        State = MdnsState.Disabled;
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
        set
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
        set
        {
            if (Set(ref progress, value))
                OnPropertyChanged(nameof(TimePassedString));
        }
    }

    private DateTime checkStart;

    private TimeSpan timePassed = TimeSpan.MinValue;

    public string TimePassedString => timePassed == TimeSpan.MinValue ? "" : Converters.UnitConverter.ToTime(timePassed.TotalSeconds, useMilli: false, digits: 0);

    public void UpdateProgress()
    {
        timePassed = DateTime.Now.Subtract(checkStart);

        Progress = timePassed < AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME
            ? timePassed / AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME * 100
            : 100;
    }

    public PairingQrClass? QrClass { get; set; }

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
