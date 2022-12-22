using ADB_Explorer.Converters;
using ADB_Explorer.Services;
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
            if (BatteryState == 0)
                return "";
            else if (byte.TryParse(BatteryState.ToString(), out _))
                return $"Status: {(ChargeSource == Source.None ? "Discharging" : $"Charging ({ChargeSource})")}";
            else
                return $"Status: {BatteryState.ToString().Replace('_', ' ')}{(ChargeSource == Source.None ? "" : $" ({ChargeSource})")}";
        }
    }

    public string CompactStateString => ChargeState is ChargingState.Unknown
                ? "Battery Status Unknown"
                : $"{Level}%{(ChargeState is ChargingState.Charging ? ", Plugged In" : "")}";

    public string VoltageString => Voltage switch
    {
        null => "",
        _ => $"Voltage: {Voltage}v"
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

            return $"Power Balance: {(positive ? "+" : "")}{(perHourConsumption / 1000000).ToSize()}Ah";
        }
    }

    public string TemperatureString => Temperature switch
    {
        null => "",
        _ => $"Temperature: {Temperature}°C"
    };

    public string BatteryHealthString => BatteryHealth switch
    {
        0 => "",
        _ => $"Health: {BatteryHealth.ToString().Replace('_', ' ')}"
    };

    public string BatteryIcon
    {
        get
        {
            if (ChargeState == ChargingState.Unknown || Level is null)
                return AppSettings.Is22H2 ? "\uEC02" : "\uF608";

            var level = AppSettings.Is22H2 ? 0xEBA0 : 0xF5F2;
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

        if (batteryInfo.ContainsKey("AC powered") && batteryInfo["AC powered"] == "true")
            ChargeSource = Source.AC;

        if (batteryInfo.ContainsKey("USB powered") && batteryInfo["USB powered"] == "true")
            ChargeSource = Source.USB;

        if (batteryInfo.ContainsKey("Wireless powered") && batteryInfo["Wireless powered"] == "true")
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

        if (batteryInfo.ContainsKey("Charge counter")
            && long.TryParse(batteryInfo["Charge counter"], out long charge))
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
}
