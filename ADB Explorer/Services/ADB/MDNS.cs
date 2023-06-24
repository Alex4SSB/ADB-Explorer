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

    public void CheckMdns()
    {
        if (ADBService.CheckMDNS())
            State = MdnsState.Running;
        else
            State = MdnsState.NotRunning;
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

    public string TimePassedString => timePassed == TimeSpan.MinValue ? "" : Converters.UnitConverter.ToTime((decimal)timePassed.TotalSeconds, useMilli: false, digits: 0);

    public void UpdateProgress()
    {
        timePassed = DateTime.Now.Subtract(checkStart);

        Progress = timePassed < AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME
            ? timePassed / AdbExplorerConst.MDNS_DOWN_RESPONSE_TIME * 100
            : 100;
    }
}
