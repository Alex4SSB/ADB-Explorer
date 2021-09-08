namespace ADB_Explorer.Models
{
    public class DeviceClass
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Offline,
            Unauthorized
        }

        public string Name { get; set; }
        public string ID { get; set; }
        public DeviceType Type { get; set; }
        public string Icon {
            get
            {
                return Type switch
                {
                    DeviceType.Local => "\uE839",
                    DeviceType.Remote => "\uEE77",
                    DeviceType.Offline => "\uEB5E",
                    DeviceType.Unauthorized => "\uF476",
                    _ => throw new System.NotImplementedException(),
                };
            }
        }
        public bool IsOpen { get; set; }

        public DeviceClass(string name, string id, DeviceType type = DeviceType.Local)
        {
            Name = name;
            ID = id;
            Type = type;
        }

        public DeviceClass(string name, string id, string status)
        {
            Name = name;
            ID = id;

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
    }
}
