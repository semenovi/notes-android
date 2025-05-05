using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Models
{
    public enum SyncProtocolType
    {
        Usb,
        Network,
        File
    }

    public class SyncProfile : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private SyncProtocolType _protocol;
        private Dictionary<string, string> _settings;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public SyncProtocolType Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        public Dictionary<string, string> Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public SyncProfile()
        {
            Id = Guid.NewGuid().ToString();
            Settings = new Dictionary<string, string>();
        }

        public bool Validate()
        {
            switch (Protocol)
            {
                case SyncProtocolType.Usb:
                    return Settings.ContainsKey("DeviceId");
                case SyncProtocolType.Network:
                    return Settings.ContainsKey("Host") && Settings.ContainsKey("Port");
                case SyncProtocolType.File:
                    return Settings.ContainsKey("Path");
                default:
                    return false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}