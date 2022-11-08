using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Services.ADBService;

namespace ADB_Explorer.Models
{
    public enum PathType
    {
        Android,
        Windows,
    }

    public enum RelationType
    {
        Ancestor,
        Descendant,
        Self,
        Unrelated,
    }

    public class FilePath : INotifyPropertyChanged
    {
        public PathType PathType { get; protected set; }

        protected bool IsRegularFile { private get; set; }
        public bool IsDirectory { get; protected set; }

        private string fullPath;
        public string FullPath
        {
            get => fullPath;
            protected set => Set(ref fullPath, value);
        }

        public string ParentPath
        {
            get
            {
                int originalIndex = FullPath.LastIndexOf(PathSeparator());
                Index index;
                if (originalIndex == 0)
                    index = 1;
                else if (originalIndex < 0)
                    index = ^0;
                else
                    index = originalIndex;

                return FullPath[..index];
            }
        }

        private string fullName;
        public string FullName
        {
            get => fullName;
            protected set => Set(ref fullName, value);
        }
        public string NoExtName
        {
            get
            {
                if (IsDirectory || !IsRegularFile || HiddenOrWithoutExt(FullName))
                    return FullName;
                else
                    return FullName[..FullName.LastIndexOf('.')];
            }
        }

        public string DisplayName => Data.Settings.ShowExtensions ? FullName : NoExtName;

        public readonly AdbDevice Device; // will be left null for PC
        
        public FilePath(ShellObject windowsPath)
        {
            PathType = PathType.Windows;

            FullPath = windowsPath.ParsingName;
            FullName = windowsPath.Name;
            IsDirectory = windowsPath is ShellFolder;
            IsRegularFile = !IsDirectory;
        }

        public FilePath(string androidPath,
                        string fullName = "",
                        FileType fileType = FileType.File,
                        AdbDevice device = null)
        {
            PathType = PathType.Android;

            FullPath = androidPath;
            FullName = string.IsNullOrEmpty(fullName) ? GetFullName(androidPath) : fullName;
            IsDirectory = fileType == FileType.Folder;
            IsRegularFile = fileType == FileType.File;

            Device = device;
        }

        public void UpdatePath(string androidPath)
        {
            FullPath = androidPath;
            FullName = GetFullName(androidPath);
        }

        private string GetFullName(string fullPath) =>
            fullPath[(fullPath.LastIndexOf(PathSeparator()) + 1)..];

        private static bool HiddenOrWithoutExt(string fullName) => fullName.Count(c => c == '.') switch
        {
            0 => true,
            1 when fullName.StartsWith('.') => true,
            _ => false,
        };

        private char PathSeparator() => PathSeparator(PathType);

        private static char PathSeparator(PathType pathType) => pathType switch
        {
            PathType.Windows => '\\',
            PathType.Android => '/',
            _ => throw new NotSupportedException(),
        };

        public RelationType Relation(FilePath other) => Relation(other.FullPath);

        public RelationType Relation(string other)
        {
            if (other == FullPath)
                return RelationType.Self;

            if (other.StartsWith(FullPath))
                return RelationType.Descendant;

            if (FullPath.StartsWith(other))
                return RelationType.Ancestor;

            return RelationType.Unrelated;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public override string ToString()
        {
            return FullName;
        }
    }
}
