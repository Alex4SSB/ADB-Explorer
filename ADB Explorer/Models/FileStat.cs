using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static ADB_Explorer.Converters.FileTypeClass;

namespace ADB_Explorer.Models
{
    public class FileStat : INotifyPropertyChanged
    {
        public FileStat(string fileName, string path, FileType type, bool isLink = false, ulong? size = null, DateTime? modifiedTime = null)
        {
            this.fileName = fileName;
            this.path = path;
            this.type = type;
            this.size = size;
            this.modifiedTime = modifiedTime;
            this.isLink = isLink;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private string fileName;
        public string FileName
        {
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

        private ulong? size;
        public ulong? Size
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

        private DateTime? modifiedTime;
        public DateTime? ModifiedTime
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

        private bool isLink;

        public bool IsLink
        {
            get
            {
                return isLink;
            }
            set
            {
                isLink = value;
                NotifyPropertyChanged();
            }
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return fileName;
        }
    }
}
