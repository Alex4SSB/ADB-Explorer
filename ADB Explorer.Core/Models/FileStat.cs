using System;

namespace ADB_Explorer.Core.Models
{
    public class FileStat
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public FileType Type { get; set; }
        public UInt64 Size { get; set; }
        public DateTime ModifiedTime { get; set; }

        public enum FileType
        {
            Drive,
            Folder,
            File,
            Parent
        }
    }
}
