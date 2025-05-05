using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Notes.Models
{
    public class MediaItem : INotifyPropertyChanged
    {
        private string _id;
        private string _fileName;
        private string _fileType;
        private string _storagePath;
        private long _size;
        private DateTime _created;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public string FileType
        {
            get => _fileType;
            set => SetProperty(ref _fileType, value);
        }

        public string StoragePath
        {
            get => _storagePath;
            set => SetProperty(ref _storagePath, value);
        }

        public long Size
        {
            get => _size;
            set => SetProperty(ref _size, value);
        }

        public DateTime Created
        {
            get => _created;
            set => SetProperty(ref _created, value);
        }

        public MediaItem()
        {
            Id = Guid.NewGuid().ToString();
            Created = DateTime.Now;
        }

        public async Task<byte[]> GetContentAsync()
        {
            if (string.IsNullOrEmpty(StoragePath) || !File.Exists(StoragePath))
                return null;
            
            return await File.ReadAllBytesAsync(StoragePath);
        }

        public async Task SaveContentAsync(byte[] data)
        {
            if (string.IsNullOrEmpty(StoragePath))
                return;
            
            await File.WriteAllBytesAsync(StoragePath, data);
            Size = data.Length;
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