using ADB_Explorer.Converters;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public class Battery : ViewModelBase
{
    #region Enums

    public enum State
    {
        Unknown = 1,
        Charging = 2,
        Discharging = 3,
        Not_Charging = 4,
        Full = 5,
    }

    public enum ChargingState
    {
        Unknown,
        Discharging,
        Charging,
    }

    public enum Health
    {
        Unknown = 1,
        Good = 2,
        Overheat = 3,
        Dead = 4,
        Over_Voltage = 5,
        Unspecified_failure = 6,
        Cold = 7,
    }

    public enum Source
    {
        None,
        AC,
        USB,
        Wireless,
    }

    #endregion

    #region Full properties

    private Source chargeSource = Source.None;
    public Source ChargeSource
    {
        get => chargeSource;
        set
        {
            if (Set(ref chargeSource, value))
                OnPropertyChanged(nameof(BatteryStateString));
        }
    }

    private State batteryState = State.Unknown;
    public State BatteryState
    {
        get => batteryState;
        set
        {
            if (Set(ref batteryState, value))
                OnPropertyChanged(nameof(BatteryStateString));
        }
    }

    private ChargingState chargeState = ChargingState.Unknown;
    public ChargingState ChargeState
    {
        get => chargeState;
        set
        {
            if (Set(ref chargeState, value))
            {
                OnPropertyChanged(nameof(BatteryIcon));
                OnPropertyChanged(nameof(CompactStateString));
            }
        }
    }

    private byte? level;
    public byte? Level
    {
        get => level;
        set
        {
            if (Set(ref level, value))
            {
                OnPropertyChanged(nameof(BatteryIcon));
                OnPropertyChanged(nameof(CompactStateString));
            }
        }
    }

    private double? voltage;
    public double? Voltage
    {
        get => voltage;
        set
        {
            if (Set(ref voltage, value))
                OnPropertyChanged(nameof(VoltageString));
        }
    }

    private double? temperature;
    public double? Temperature
    {
        get => temperature;
        set
        {
            if (Set(ref temperature, value))
                OnPropertyChanged(nameof(TemperatureString));
        }
    }

    private Health batteryHealth = 0;
    public Health BatteryHealth
    {
        get => batteryHealth;
        set
        {
            if (Set(ref batteryHealth, value))
                OnPropertyChanged(nameof(BatteryHealthString));
        }
    }

    #endregion

    private long? chargeCounter = null;
    private long? prevChargeCounter = null;
    private DateTime? chargeUpdate = null;
    private DateTime? prevChargeUpdate = null;

    #region Read only properties

    public string BatteryStateString
    {
        get
        {
            if (BatteryState is 0)
                return "";

            var status = byte.TryParse(BatteryState.ToString(), out _)
                ? $"{(ChargeSource is Source.None ? Strings.Resources.S_BAT_STATE_DISCHARGING : $"{Strings.Resources.S_BAT_STATE_CHARGING} ({SourceString(ChargeSource)})")}"
                : $"{StateString(BatteryState)}{(ChargeSource is Source.None ? "" : $" ({SourceString(ChargeSource)})")}";

            return string.Format(Strings.Resources.S_BAT_STATUS, status);
        }
    }

    public string CompactStateString
    {
        get
        {
            if (ChargeState is ChargingState.Unknown)
            {
                return Strings.Resources.S_BAT_STATUS_UNKNOWN;
            }
            else
            {
                if (ChargeState is not ChargingState.Charging)
                    return $"{Level}%";

                return string.Format(Strings.Resources.S_BAT_STATUS_PLUGGED, Level);
            }
        }
    }

    public string VoltageString => Voltage switch
    {
        null => "",
        _ => string.Format(Strings.Resources.S_BAT_VOLT, Voltage)
    };

    public string CurrentConsumption
    {
        get
        {
            if (chargeCounter is null || prevChargeCounter is null || prevChargeUpdate is null)
                return "";

            var currentDiff = chargeCounter.Value - prevChargeCounter.Value;
            var timeDiff = DateTime.Now - prevChargeUpdate.Value;
            var perHourConsumption = currentDiff / timeDiff.TotalHours;
            var positive = perHourConsumption > 0;

            return string.Format(Strings.Resources.S_BAT_BALANCE, $"{(positive ? "+" : "")}{(perHourConsumption / 1000000).AmpsToSize()}");
        }
    }

    public string TemperatureString => Temperature switch
    {
        null => "",
        _ => string.Format(Strings.Resources.S_BAT_TEMP, Temperature)
    };

    public string BatteryHealthString
    {
        get
        {
            if (BatteryHealth is 0)
                return "";
            
            string healthString = BatteryHealth switch
            {
                Health.Unknown => Strings.Resources.S_BAT_STATE_UNKNOWN,
                Health.Good => Strings.Resources.S_BAT_HEALTH_GOOD,
                Health.Overheat => Strings.Resources.S_BAT_HEALTH_OVERHEAT,
                Health.Dead => Strings.Resources.S_BAT_HEALTH_DEAD,
                Health.Over_Voltage => Strings.Resources.S_BAT_HEALTH_OVER_VOLTAGE,
                Health.Unspecified_failure => Strings.Resources.S_BAT_HEALTH_UNSPECIFIED_FAILURE,
                Health.Cold => Strings.Resources.S_BAT_HEALTH_COLD,
                _ => null,
            };

            return string.Format(Strings.Resources.S_BAT_HEALTH, healthString);
        }
    }

    public string BatteryIcon
    {
        get
        {
            if (ChargeState == ChargingState.Unknown || Level is null)
                return Data.RuntimeSettings.Is22H2 ? "\uEC02" : "\uF608";

            var level = Data.RuntimeSettings.Is22H2 ? 0xEBA0 : 0xF5F2;
            if (ChargeState == ChargingState.Charging)
                level += 11;

            level += Level.Value / 10;

            return $"{Convert.ToChar(level)}";
        }
    }

    #endregion

    public Battery()
    {
    }

    public void Update(Dictionary<string, string> batteryInfo)
    {
        if (batteryInfo is null)
            return;

        if (batteryInfo.TryGetValue("AC powered", out string ac) && ac == "true")
            ChargeSource = Source.AC;

        if (batteryInfo.TryGetValue("USB powered", out string usb) && usb == "true")
            ChargeSource = Source.USB;

        if (batteryInfo.TryGetValue("Wireless powered", out string wl) && wl == "true")
            ChargeSource = Source.Wireless;

        if (batteryInfo.ContainsKey("status"))
        {
            BatteryState = !byte.TryParse(batteryInfo["status"], out byte status)
                ? State.Unknown
                : (State)status;

            ChargeState = status switch
            {
                <= 1 => ChargingState.Unknown,
                3 or 4 => ChargingState.Discharging,
                2 or 5 => ChargingState.Charging,
                > 5 when ChargeSource == Source.None => ChargingState.Discharging,
                > 5 => ChargingState.Charging,
            };
        }

        if (batteryInfo.ContainsKey("level"))
        {
            Level = !byte.TryParse(batteryInfo["level"], out byte level)
                ? null
                : level;
        }

        if (batteryInfo.ContainsKey("voltage"))
        {
            Voltage = !int.TryParse(batteryInfo["voltage"], out int volt)
                ? -1.0
                : volt / 1000.0;
        }

        if (batteryInfo.ContainsKey("temperature"))
        {
            Temperature = !int.TryParse(batteryInfo["temperature"], out int temp)
                ? -1.0
                : temp / 10;
        }

        if (batteryInfo.ContainsKey("health"))
        {
            BatteryHealth = !Enum.TryParse(typeof(Health), batteryInfo["health"], out object health)
                ? Health.Unknown
                : (Health)health;
        }

        if (batteryInfo.TryGetValue("Charge counter", out string value)
            && long.TryParse(value, out long charge))
        {
            if (chargeCounter != charge || prevChargeUpdate is null)
            {
                prevChargeUpdate = chargeUpdate;
                chargeUpdate = DateTime.Now;
                prevChargeCounter = chargeCounter;
                chargeCounter = charge;
            }
        }

        OnPropertyChanged(nameof(CurrentConsumption));
    }

    public static string SourceString(Source source) => source switch
    {
        Source.None => Strings.Resources.S_BAT_SOURCE_NONE,
        Source.AC => Strings.Resources.S_BAT_SOURCE_AC,
        Source.USB => Strings.Resources.S_TYPE_USB,
        Source.Wireless => Strings.Resources.S_BAT_SOURCE_WIRELESS,
        _ => null,
    };

    public static string StateString(State state) => state switch
    {
        State.Unknown => Strings.Resources.S_BAT_STATE_UNKNOWN,
        State.Charging => Strings.Resources.S_BAT_STATE_CHARGING,
        State.Discharging => Strings.Resources.S_BAT_STATE_DISCHARGING,
        State.Not_Charging => Strings.Resources.S_BAT_STATE_NOT_CHARGING,
        State.Full => Strings.Resources.S_BAT_STATE_FULL,
        _ => null,
    };
}
