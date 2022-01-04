using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ADB_Explorer.Services
{
    public class MDNS : INotifyPropertyChanged
    {
        public MDNS()
        {
            State = MdnsState.Disabled;
        }

        public enum MdnsState
        {
            Disabled,
            Unchecked,
            NotRunning,
            Running,
        }

        private MdnsState state;
        public MdnsState State
        {
            get => state;
            set
            {
                state = value;
                NotifyPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void CheckMdns()
        {
            if (ADBService.CheckMDNS())
                State = MdnsState.Running;
            else
                State = MdnsState.NotRunning;
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
