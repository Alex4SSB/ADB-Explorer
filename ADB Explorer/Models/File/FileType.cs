namespace ADB_Explorer.Converters
{
    public static class FileTypeClass
    {
        private static readonly string[] names = { "Socket", "File", "Block Device", "Folder", "Char Device", "FIFO", "Unknown" };
        public enum FileType
        {
            Socket = 0,
            File = 1,
            BlockDevice = 2,
            Folder = 3,
            CharDevice = 4,
            FIFO = 5,
            Unknown = 6,
        }

        public static string Name(this FileType type) => names[(int)type];
    }
}
