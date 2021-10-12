using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ADB_Explorer.Models
{
    public static class Devices
    {
        public static List<DeviceClass> List { get; private set; } = new();

        public static DeviceClass Current => List?.Find(device => device.IsOpen);

        public static bool Update(IEnumerable<DeviceClass> other)
        {
            bool isCurrentTypeUpdated = false;

            // First remove all devices that no longer exist
            List.RemoveAll(thisDevice => !other.Any(otherDevice => otherDevice.ID == thisDevice.ID));

            // Then update existing devices' types and names
            foreach (var item in other)
            {
                if (List?.Find(thisDevice => thisDevice.ID == item.ID) is DeviceClass device)
                {
                    // Return (at the end of the function) true if current device type has changed
                    if (device.IsOpen && device.Type != item.Type)
                        isCurrentTypeUpdated = true;

                    device.UpdateDevice(item);
                }
                else
                {
                    // And add the new devices
                    List.Add(item);
                }
            }

            return isCurrentTypeUpdated;
        }

        private static IEnumerable<DeviceClass> AvailableDevices(bool current = false)
        {
            return List.Where(device => (!current || device.IsOpen)
                && device.Type is DeviceClass.DeviceType.Local or DeviceClass.DeviceType.Remote);
        }

        public static bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

        public static bool SetCurrentDevice(string selectedId)
        {
            var availableDevices = AvailableDevices();

            if (availableDevices.Count() > 1)
            {
                availableDevices = availableDevices.Where(device => device.ID == selectedId);
            }

            if (availableDevices.Any())
            {
                availableDevices.First().SetOpen();
                return true;
            }
            else
                return false;
        }

        public static bool DevicesChanged(IEnumerable<DeviceClass> other)
        {
            return other is not null
                && !List.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(other.OrderBy(otherDevice => otherDevice.ID), new DeviceTypeEqualityComparer());
        }

        public static void UnselectAll()
        {
            DeviceClass.SetSelected();
        }
    }


    public class DeviceClass
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Offline,
            Unauthorized
        }

        public string Name { get; private set; }
        public string ID { get; private set; }
        public DeviceType Type { get; private set; }
        public string Icon => Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Offline => "\uEB5E",
            DeviceType.Unauthorized => "\uF476",
            _ => throw new System.NotImplementedException(),
        };
        public bool IsOpen { get; private set; }
        public string Size { get; set; }
        public string Used { get; set; }
        public string AvailableP { get; set; }

        public void SetOpen(bool openState = true)
        {
            Devices.List.ForEach(device => device.IsOpen =
                device == this && openState);
        }

        public bool IsSelected { get; private set; }

        public void SetSelected(bool selectedState = true)
        {
            Devices.List.ForEach(device => device.IsSelected =
                device == this && selectedState);
        }

        public static void SetSelected()
        {
            Devices.List.ForEach(device => device.IsSelected = false);
        }

        public override bool Equals(object obj)
        {
            return obj is DeviceClass device &&
                   ID == device.ID;
        }

        public DeviceClass(string name, string id, DeviceType type = DeviceType.Local)
        {
            Name = name;
            ID = id;
            Type = type;

            if (string.IsNullOrEmpty(Name))
                Name = "[Unauthorized]";
        }

        public DeviceClass(string name, string id, string status) : this(name, id)
        {
            Type = status switch
            {
                "device" when id.Contains('.') => DeviceType.Remote,
                "device" => DeviceType.Local,
                "offline" => DeviceType.Offline,
                "unauthorized" => DeviceType.Unauthorized,
                _ => throw new System.NotImplementedException(),
            };
        }

        public DeviceClass()
        {
        }

        public static implicit operator bool(DeviceClass obj)
        {
            return obj is object && !string.IsNullOrEmpty(obj.ID);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public void UpdateDevice(DeviceClass other)
        {
            Type = other.Type;
            Name = other.Name;
        }

        internal void SetSize(Tuple<string, string, string> size)
        {
            Size = size.Item1;
            Used = size.Item2;
            AvailableP = size.Item3;
        }
    }

    public class DeviceTypeEqualityComparer : IEqualityComparer<DeviceClass>
    {
        public bool Equals(DeviceClass x, DeviceClass y)
        {
            return x.ID == y.ID && x.Type == y.Type;
        }

        public int GetHashCode([DisallowNull] DeviceClass obj)
        {
            throw new System.NotImplementedException();
        }
    }
}
