using System;
using System.Globalization;
using System.IO;
using ADB_Explorer.Converters;
using ADB_Explorer.Core.Models;

namespace ADB_Explorer.Models
{
    public class FileClass : FileStat
    {
        public FileClass() { }

        public FileClass(FileStat fileStat)
        {
            Name = fileStat.Name;
            Path = fileStat.Path;
            Type = fileStat.Type;
            Size = fileStat.Size;
            ModifiedTime = fileStat.ModifiedTime;
        }

        public object Icon { get; }
        public string TypeName { get; set; }
        public string Date => ModifiedTime.ToString(CultureInfo.CurrentCulture.DateTimeFormat);
        public string SizeString => Size.ToSize();
    }

    public class PhysicalFileClass : FileClass
    {
        public PhysicalFileClass(string path, FileType type)
        {
            Path = path;
            Type = type;
            TypeName = type.ToString();

            Name = type switch
            {
                FileType.Drive => path,
                FileType.Folder => System.IO.Path.GetFileName(path),
                FileType.File => System.IO.Path.GetFileNameWithoutExtension(path),
                FileType.Parent => "..",
                _ => throw new NotImplementedException()
            };
        }
    }
}
