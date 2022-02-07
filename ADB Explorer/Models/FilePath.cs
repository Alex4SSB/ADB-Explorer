using System;
using System.IO;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Services.ADBService;

namespace ADB_Explorer.Models
{
    public enum PathType
    {
        Android,
        Windows,
    }

    public class FilePath
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
                        PathType.Android => FileObject.Type == FileType.Folder,
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
                    || (Type is PathType.Android && (FileObject.Type is not FileType.File   // do not trim if not a regular file
                    || (FullName.StartsWith('.') && FullName.Split('.').Length == 2))))     // don't try to trim the name of a hidden file that has no extension
                    return FullName;
                else
                    return FullName[..FullName.LastIndexOf('.')];
            }
        }

        public readonly AdbDevice Device; // will be left null for PC
        public readonly FileStat FileObject; // will be left null for PC

        public FilePath(string windowsPath)
        {
            FullPath = windowsPath;
            FullName = FullPath[FullPath.LastIndexOf('\\')..];

            Type = PathType.Windows;
        }

        public FilePath(FileStat fileClass, AdbDevice device = null)
        {
            FileObject = fileClass;
            FullPath = fileClass.Path;
            FullName = fileClass.FileName;
            Device = device;

            Type = PathType.Android;
        }
    }
}
