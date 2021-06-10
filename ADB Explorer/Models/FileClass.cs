using System;
using System.IO;

namespace ADB_Explorer.Models
{
    public class FileClass
    {
        public object Icon { get; }

        private string filePath;
        public string FilePath { get { return filePath; } }

        public string Name { get; }

        private DateTime date;
        public string Date { get; }

        private FileType type;
        public string Type { get; }

        private long size;
        public string Size { get; }

        public FileClass()
        {
        }

        public FileClass(string path, FileType type = FileType.File)
        {
            filePath = path;

            Name = type switch
            {
                FileType.Drive => path,
                FileType.Folder => Path.GetFileName(path),
                FileType.File => Path.GetFileNameWithoutExtension(path),
                FileType.Parent => "..",
                _ => throw new NotImplementedException()
            };

            this.type = type;
            Type = type.ToString();
        }
    }

    public enum FileType
    {
        Drive,
        Folder,
        File,
        Parent
    }
}
