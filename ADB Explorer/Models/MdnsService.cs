namespace ADB_Explorer.Models
{
    public class MdnsService // Consider integrating into Device
    {
        public MdnsService()
        {
        }

        public enum ServiceType
        {
            QrCode,
            PairingCode
        }

        public string ID { get; set; }
        public string IpAddress { get; set; }
        public string PairingPort { get; set; }
        public string ConnectPort { get; set; }
        public ServiceType Type { get; set; }

    }
}
