namespace ADB_Explorer.Models
{
    public class DeviceClass
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Offline
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
                    _ => throw new System.NotImplementedException(),
                };
            }
        }
        public bool IsConnected { get; set; }

        public DeviceClass(string name, string id, DeviceType type = DeviceType.Local)
        {
            Name = name;
            ID = id;
            Type = type;
        }

        public DeviceClass()
        {
        }
    }
}
