using System;
using System.IO;
using static ADB_Explorer.Converters.FileTypeClass;

namespace ADB_Explorer.Models
{
    public enum PathType
    {
        Android,
        Windows,
    }

    internal class FilePath
    {
        public PathType Type { get; set; }

        private bool? isDirectory;
        public bool IsDirectory
        {
            get
            {
                if (isDirectory is null)
                {
                    isDirectory = Type switch
                    {
                        PathType.Android => fileClass.Type == FileType.Folder,
                        PathType.Windows => Directory.Exists(FullPath),
                        _ => throw new NotImplementedException(),
                    };
                }
                return isDirectory.Value;
            }
        }

        public string FullPath { get; set; }
        public string ParentPath => FullPath[..FullPath.LastIndexOf(Type is PathType.Windows ? '\\' : '/')];
        public readonly string FullName;
        public string NoExtName
        {
            get
            {
                if (IsDirectory                                                             // directories do not have extensions
                    || (Type is PathType.Android && (fileClass.Type is not FileType.File    // do not trim if not a regular file
                    || (FullName.StartsWith('.') && FullName.Split('.').Length == 2))))     // don't try to trim the name of a hidden file that has no extension
                    return FullName;
                else
                    return FullName[..FullName.LastIndexOf('.')];
            }
        }

        public LogicalDevice Device { get; set; } // will be left null for PC
        private readonly FileStat fileClass; // will be left null for PC

        public FilePath(string fullPath)
        {
            FullPath = fullPath;
            FullName = FullPath[FullPath.LastIndexOf('\\')..];

            Type = PathType.Windows;
        }

        public FilePath(FileStat fileClass, LogicalDevice device = null)
        {
            this.fileClass = fileClass;
            FullPath = fileClass.Path;
            FullName = fileClass.FileName;
            Device = device;

            Type = PathType.Android;
        }
    }
}
