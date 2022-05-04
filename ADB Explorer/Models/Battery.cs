using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Models
{
    public class Battery : INotifyPropertyChanged
    {
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

        private Source chargeSource { get; set; } = Source.None;
        private State batteryState { get; set; } = State.Unknown;
        public string BatteryState
        {
            get
            {
                if (batteryState == 0)
                    return "";
                else if (byte.TryParse(batteryState.ToString(), out _))
                    return $"Status: {(chargeSource == Source.None ? "Discharging" : $"Charging ({chargeSource})")}";
                else
                    return $"Status: {batteryState.ToString().Replace('_', ' ')}{(chargeSource == Source.None ? "" : $" ({chargeSource})")}";
            }
        }

        public ChargingState ChargeState { get; private set; } = ChargingState.Unknown;
        public byte? Level { get; private set; }
        private double? voltage { get; set; }
        public string Voltage
        {
            get
            {
                if (voltage is null)
                    return "";
                else
                    return $"Voltage: {voltage}v";
            }
        }

        private double? temperature { get; set; }
        public string Temperature
        {
            get
            {
                if (temperature is null)
                    return "";
                else
                    return $"Temperature: {temperature}°C";
            }
        }

        private Health batteryHealth { get; set; } = 0;
        public string BatteryHealth
        {
            get
            {
                if (batteryHealth == 0)
                    return "";
                else
                    return $"Health: {batteryHealth.ToString().Replace('_', ' ')}";
            }
        }

        public string BatteryIcon
        {
            get
            {
                if (ChargeState == ChargingState.Unknown || Level is null)
                    return "\uEC02";

                var level = 0xEBA0;
                if (ChargeState == ChargingState.Charging)
                    level += 11;

                level += Level.Value / 10;

                return $"{Convert.ToChar(level)}";
            }
        }

        public Battery(Dictionary<string, string> batteryInfo)
        {
            if (batteryInfo is null)
                return;

            if (batteryInfo.ContainsKey("AC powered") && batteryInfo["AC powered"] == "true")
                chargeSource = Source.AC;

            if (batteryInfo.ContainsKey("USB powered") && batteryInfo["USB powered"] == "true")
                chargeSource = Source.USB;

            if (batteryInfo.ContainsKey("Wireless powered") && batteryInfo["Wireless powered"] == "true")
                chargeSource = Source.Wireless;

            if (batteryInfo.ContainsKey("status"))
            {
                if (!byte.TryParse(batteryInfo["status"], out byte status))
                    batteryState = State.Unknown;
                else
                {
                    batteryState = (State)status;
                }

                ChargeState = status switch
                {
                    <= 1 => ChargingState.Unknown,
                    3 or 4 => ChargingState.Discharging,
                    2 or 5 => ChargingState.Charging,
                    > 5 when chargeSource == Source.None => ChargingState.Discharging,
                    > 5 => ChargingState.Charging,
                };
            }

            if (batteryInfo.ContainsKey("level"))
            {
                if (!byte.TryParse(batteryInfo["level"], out byte level))
                    Level = null;
                else
                    Level = level;
            }

            if (batteryInfo.ContainsKey("voltage"))
            {
                if (!int.TryParse(batteryInfo["voltage"], out int volt))
                    voltage = -1;
                else
                    voltage = volt / 1000.0;
            }

            if (batteryInfo.ContainsKey("temperature"))
            {
                if (!int.TryParse(batteryInfo["temperature"], out int temp))
                    temperature = -1;
                else
                    temperature = temp / 10;
            }

            if (batteryInfo.ContainsKey("health"))
            {
                if (!Enum.TryParse(typeof(Health), batteryInfo["health"], out object health))
                    batteryHealth = Health.Unknown;
                else
                    batteryHealth = (Health)health;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
