namespace ADB_Explorer.Models;

public class Battery : ObservableObject
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

    private Source _chargeSource = Source.None;
    public Source ChargeSource
    {
        get => _chargeSource;
        set
        {
            if (FieldHelper.TrySet(ref _chargeSource, value))
                OnPropertyChanged(nameof(BatteryStateString));
        }
    }

    private State _batteryState = State.Unknown;
    public State BatteryState
    {
        get => _batteryState;
        set
        {
            if (FieldHelper.TrySet(ref _batteryState, value))
                OnPropertyChanged(nameof(BatteryStateString));
        }
    }

    private ChargingState _chargeState = ChargingState.Unknown;
    public ChargingState ChargeState
    {
        get => _chargeState;
        set
        {
            if (SetProperty(ref _chargeState, value))
            {
                OnPropertyChanged(nameof(BatteryIcon));
                OnPropertyChanged(nameof(CompactStateString));
                OnPropertyChanged(nameof(BatteryLow));
                OnPropertyChanged(nameof(FullyCharged));
            }
        }
    }

    private byte? _level;
    public byte? Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
            {
                OnPropertyChanged(nameof(BatteryIcon));
                OnPropertyChanged(nameof(CompactStateString));
                OnPropertyChanged(nameof(BatteryLow));
                OnPropertyChanged(nameof(FullyCharged));
            }
        }
    }

    private double? _voltage;
    public double? Voltage
    {
        get => _voltage;
        set
        {
            if (FieldHelper.TrySet(ref _voltage, value))
                OnPropertyChanged(nameof(VoltageString));
        }
    }

    private double? _temperature;
    public double? Temperature
    {
        get => _temperature;
        set
        {
            if (FieldHelper.TrySet(ref _temperature, value))
                OnPropertyChanged(nameof(TemperatureString));
        }
    }

    private Health _batteryHealth = 0;
    public Health BatteryHealth
    {
        get => _batteryHealth;
        set
        {
            if (FieldHelper.TrySet(ref _batteryHealth, value))
                OnPropertyChanged(nameof(BatteryHealthString));
        }
    }

    #endregion

    private long? _chargeCounter = null;
    private long? _prevChargeCounter = null;
    private DateTime? _chargeUpdate = null;
    private DateTime? _prevChargeUpdate = null;
    private long? _currentNowMicroAmps = null;

    #region Read only properties

    public bool BatteryLow => Level is not null && Level <= 10 && ChargeState is not ChargingState.Charging;
    public bool FullyCharged => Level is not null && Level >= 100 && ChargeState is ChargingState.Charging;

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
                    return Data.RuntimeSettings.IsRTL
                        ? $"%{Level}"
                        : $"{Level}%";

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
            if (_currentNowMicroAmps is long now)
                return FormatCurrentBalance(now);

            if (_chargeCounter is null || _prevChargeCounter is null || _prevChargeUpdate is null)
                return "";

            var currentDiff = _chargeCounter.Value - _prevChargeCounter.Value;
            var timeDiff = DateTime.Now - _prevChargeUpdate.Value;
            if (timeDiff.TotalSeconds < 1)
                return "";

            var perHourConsumption = currentDiff / timeDiff.TotalHours;
            return FormatCurrentBalance((long)perHourConsumption);
        }
    }

    private static string FormatCurrentBalance(long microAmpsPerHourOrNow)
    {
        var amps = microAmpsPerHourOrNow / 1_000_000.0;
        var positive = amps > 0;
        return string.Format(Strings.Resources.S_BAT_BALANCE, $"{(positive ? "+" : "")}{amps.AmpsToSize()}");
    }

    private static long NormalizeCurrentSign(long microAmps, ChargingState state) => state switch
    {
        ChargingState.Charging when microAmps < 0 => -microAmps,
        ChargingState.Discharging when microAmps > 0 => -microAmps,
        _ => microAmps,
    };

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

    public Geometry BatteryIcon
    {
        get
        {
            if (ChargeState == ChargingState.Unknown || Level is null)
                return FluentBatteryGeometries.Unknown;

            return FluentBatteryGeometries.Get(Level.Value, ChargeState == ChargingState.Charging);
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

        ChargeSource = Source.None;

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

        if (batteryInfo.TryGetValue("Current now", out string currentNow)
            && long.TryParse(currentNow, out long microAmps))
        {
            _currentNowMicroAmps = NormalizeCurrentSign(microAmps, ChargeState);
        }
        else
        {
            _currentNowMicroAmps = null;
        }

        if (batteryInfo.TryGetValue("Charge counter", out string value)
            && long.TryParse(value, out long charge))
        {
            if (_chargeCounter != charge || _prevChargeUpdate is null)
            {
                _prevChargeUpdate = _chargeUpdate;
                _chargeUpdate = DateTime.Now;
                _prevChargeCounter = _chargeCounter;
                _chargeCounter = charge;
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
