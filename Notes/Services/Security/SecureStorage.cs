using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Notes.Services.Storage;

namespace Notes.Services.Security
{
    public class SecureStorageImpl
    {
        private readonly CryptoService _cryptoService;
        private readonly FileSystemStorage _storage;
        private readonly string _secureFolder = "secure";
        private readonly string _indexFile = "secure_index.json";
        private Dictionary<string, string> _secureIndex;

        public SecureStorageImpl(CryptoService cryptoService, FileSystemStorage storage)
        {
            _cryptoService = cryptoService;
            _storage = storage;
            _storage.CreateDirectory(_secureFolder);
            _secureIndex = new Dictionary<string, string>();
            LoadSecureIndex();
        }

        public async Task SaveSecureDataAsync(string key, byte[] data)
        {
            if (!_cryptoService.IsInitialized)
                throw new InvalidOperationException("Encryption not initialized");

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            // Generate a unique file name
            string fileName = $"{Guid.NewGuid().ToString("N")}.dat";
            string filePath = Path.Combine(_secureFolder, fileName);

            // Encrypt the data
            byte[] encryptedData = _cryptoService.Encrypt(data);

            // Save the encrypted data
            await _storage.WriteFileAsync(filePath, encryptedData);

            // Update the index
            _secureIndex[key] = fileName;
            await SaveSecureIndexAsync();
        }

        public async Task<byte[]> GetSecureDataAsync(string key)
        {
            if (!_cryptoService.IsInitialized)
                throw new InvalidOperationException("Encryption not initialized");

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            if (!_secureIndex.TryGetValue(key, out string fileName))
                return null;

            string filePath = Path.Combine(_secureFolder, fileName);
            byte[] encryptedData = await _storage.ReadFileAsync(filePath);

            if (encryptedData == null || encryptedData.Length == 0)
                return null;

            // Decrypt the data
            return _cryptoService.Decrypt(encryptedData);
        }

        public async Task DeleteSecureDataAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            if (!_secureIndex.TryGetValue(key, out string fileName))
                return;

            string filePath = Path.Combine(_secureFolder, fileName);
            _storage.DeleteFile(filePath);

            // Update the index
            _secureIndex.Remove(key);
            await SaveSecureIndexAsync();
        }

        public bool ContainsKey(string key)
        {
            return !string.IsNullOrEmpty(key) && _secureIndex.ContainsKey(key);
        }

        private void LoadSecureIndex()
        {
            try
            {
                var indexData = _storage.ReadFileAsync(_indexFile).Result;
                if (indexData != null && _cryptoService.IsInitialized)
                {
                    // Try to decrypt if crypto is initialized
                    try
                    {
                        var decryptedData = _cryptoService.Decrypt(indexData);
                        var json = Encoding.UTF8.GetString(decryptedData);
                        _secureIndex = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    }
                    catch
                    {
                        _secureIndex = new Dictionary<string, string>();
                    }
                }
                else
                {
                    _secureIndex = new Dictionary<string, string>();
                }
            }
            catch
            {
                _secureIndex = new Dictionary<string, string>();
            }
        }

        private async Task SaveSecureIndexAsync()
        {
            if (!_cryptoService.IsInitialized)
                return;

            var json = JsonSerializer.Serialize(_secureIndex);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = _cryptoService.Encrypt(bytes);
            await _storage.WriteFileAsync(_indexFile, encryptedBytes);
        }
    }
}