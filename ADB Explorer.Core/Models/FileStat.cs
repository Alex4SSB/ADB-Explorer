using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Core.Models
{
    public class FileStat : INotifyPropertyChanged
    {
        public enum FileType
        {
            Drive,
            Folder,
            File,
            Parent
        }

        public FileStat(string fileName, string path, FileType type, UInt64 size, DateTime modifiedTime)
        {
            this.fileName = fileName;
            this.path = path;
            this.type = type;
            this.size = size;
            this.modifiedTime = modifiedTime;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private string fileName;
        public string FileName {
            get
            {
                return fileName;
            }
            set
            {
                if (value != this.fileName)
                {
                    this.fileName = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private string path;
        public string Path
        {
            get
            {
                return path;
            }
            set
            {
                if (value != this.path)
                {
                    this.path = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private FileType type;
        public FileType Type
        {
            get
            {
                return type;
            }
            set
            {
                if (value != this.type)
                {
                    this.type = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private UInt64 size;
        public UInt64 Size
        {
            get
            {
                return size;
            }
            set
            {
                if (value != this.size)
                {
                    this.size = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private DateTime modifiedTime;
        public DateTime ModifiedTime
        {
            get
            {
                return modifiedTime;
            }
            set
            {
                if (value != this.modifiedTime)
                {
                    this.modifiedTime = value;
                    NotifyPropertyChanged();
                }
            }
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
