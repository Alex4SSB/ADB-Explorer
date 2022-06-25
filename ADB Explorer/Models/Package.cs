using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADB_Explorer.Models
{
    public class Package : INotifyPropertyChanged
    {
        public enum PackageType
        {
            System,
            User,
        }
        
        private string name;
        public string Name
        {
            get => name;
            set => Set(ref name, value);
        }

        private PackageType type;
        public PackageType Type
        {
            get => type;
            set => Set(ref type, value);
        }

        private long? uid = null;
        public long? Uid
        {
            get => uid;
            set => Set(ref uid, value);
        }

        private long? version = null;
        public long? Version
        {
            get => version;
            set => Set(ref version, value);
        }

        public static Package New(string package, PackageType type)
        {
            var match = AdbRegEx.PACKAGE_LISTING.Match(package);
            if (!match.Success)
                return null;

            return new Package(match.Groups["package"].Value, type, match.Groups["uid"].Value, match.Groups["version"].Value);
        }

        public Package(string name, PackageType type, string uid, string version)
        {
            Name = name;
            Type = type;

            if (long.TryParse(uid, out long resU))
                Uid = resU;

            if (long.TryParse(version, out long resV))
                Version = resV;
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
    }
}
