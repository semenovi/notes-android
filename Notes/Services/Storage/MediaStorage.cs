using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Notes.Models;

namespace Notes.Services.Storage
{
    public class MediaStorage
    {
        private readonly FileSystemStorage _storage;
        private readonly string _metadataFile = "media_index.json";
        private readonly string _mediaFolder = "media";
        private Dictionary<string, MediaItem> _mediaItems;

        public string StoragePath => Path.Combine(_storage.RootPath, _mediaFolder);

        public MediaStorage(FileSystemStorage storage)
        {
            _storage = storage;
            _storage.CreateDirectory(_mediaFolder);
            _mediaItems = new Dictionary<string, MediaItem>();
            LoadMediaIndex();
        }

        public async Task<MediaItem> AddMediaAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            // Read the file content
            byte[] fileContent = await File.ReadAllBytesAsync(filePath);
            
            // Calculate hash to use as ID and for deduplication
            string hash = CalculateHash(fileContent);
            
            // Check if the file already exists in our storage
            if (_mediaItems.ContainsKey(hash))
                return _mediaItems[hash];

            // Create a new media item
            var mediaItem = new MediaItem
            {
                Id = hash,
                FileName = Path.GetFileName(filePath),
                FileType = Path.GetExtension(filePath),
                StoragePath = Path.Combine(_mediaFolder, hash + Path.GetExtension(filePath)),
                Size = fileContent.Length,
                Created = DateTime.Now
            };

            // Save the file to our storage
            await _storage.WriteFileAsync(mediaItem.StoragePath, fileContent);
            
            // Add to our media index
            _mediaItems.Add(mediaItem.Id, mediaItem);
            await SaveMediaIndexAsync();
            
            return mediaItem;
        }

        public bool DeleteMedia(string mediaId)
        {
            if (!_mediaItems.ContainsKey(mediaId))
                return false;

            var mediaItem = _mediaItems[mediaId];
            bool deleted = _storage.DeleteFile(mediaItem.StoragePath);
            if (deleted)
            {
                _mediaItems.Remove(mediaId);
                SaveMediaIndexAsync().Wait();
            }
            
            return deleted;
        }

        public MediaItem GetMedia(string mediaId)
        {
            if (_mediaItems.ContainsKey(mediaId))
                return _mediaItems[mediaId];
                
            return null;
        }

        public List<MediaItem> GetAllMedia()
        {
            return _mediaItems.Values.ToList();
        }

        private string CalculateHash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(data);
                return Convert.ToHexString(hashBytes).ToLower();
            }
        }

        private void LoadMediaIndex()
        {
            try
            {
                var indexData = _storage.ReadFileAsync(_metadataFile).Result;
                if (indexData != null)
                {
                    var items = JsonSerializer.Deserialize<List<MediaItem>>(indexData);
                    _mediaItems = items.ToDictionary(i => i.Id);
                }
            }
            catch
            {
                _mediaItems = new Dictionary<string, MediaItem>();
            }
        }

        private async Task SaveMediaIndexAsync()
        {
            var items = _mediaItems.Values.ToList();
            var json = JsonSerializer.Serialize(items);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _storage.WriteFileAsync(_metadataFile, bytes);
        }
    }
}