using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Notes.Models;

namespace Notes.Services.Sync
{
    public class UsbSyncAdapter : SyncAdapter
    {
        private string _syncFolderPath;
        private const string MANIFEST_FILE = "sync_manifest.json";
        private const string CHANGES_FILE = "sync_changes.json";
        private const string MEDIA_FOLDER = "media";

        public UsbSyncAdapter()
        {
            ProtocolType = SyncProtocolType.Usb;
            IsConnected = false;
        }

        public override async Task ConnectAsync(SyncProfile profile)
        {
            if (profile == null || profile.Protocol != SyncProtocolType.Usb)
                throw new ArgumentException("Invalid sync profile for USB protocol");

            if (!profile.Settings.TryGetValue("Path", out string path))
                throw new InvalidOperationException("Path not specified in sync profile");

            if (!Directory.Exists(path))
                throw new InvalidOperationException($"Sync folder does not exist: {path}");

            _syncFolderPath = path;
            IsConnected = true;

            // Create necessary folders
            Directory.CreateDirectory(Path.Combine(_syncFolderPath, MEDIA_FOLDER));
        }

        public override Task DisconnectAsync()
        {
            IsConnected = false;
            _syncFolderPath = null;
            return Task.CompletedTask;
        }

        public override async Task<List<SyncChange>> GetChangesAsync()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            string changesFilePath = Path.Combine(_syncFolderPath, CHANGES_FILE);
            if (!File.Exists(changesFilePath))
                return new List<SyncChange>();

            try
            {
                string json = await File.ReadAllTextAsync(changesFilePath);
                var changes = JsonSerializer.Deserialize<List<SyncChange>>(json);

                // Load binary data for media items
                foreach (var change in changes)
                {
                    if (change.ItemType == SyncItemType.Media && change.ChangeType != SyncChangeType.Deleted)
                    {
                        string mediaFilePath = Path.Combine(_syncFolderPath, MEDIA_FOLDER, change.Id);
                        if (File.Exists(mediaFilePath))
                        {
                            change.BinaryData = await File.ReadAllBytesAsync(mediaFilePath);
                        }
                    }
                }

                return changes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading sync changes: {ex.Message}");
                return new List<SyncChange>();
            }
        }

        public override async Task ApplyChangesAsync(List<SyncChange> changes)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            // Save the changes file
            string changesFilePath = Path.Combine(_syncFolderPath, CHANGES_FILE);
            string json = JsonSerializer.Serialize(changes);
            await File.WriteAllTextAsync(changesFilePath, json);

            // Save media binary data
            foreach (var change in changes)
            {
                if (change.ItemType == SyncItemType.Media && change.ChangeType != SyncChangeType.Deleted)
                {
                    var mediaItem = change.Data as MediaItem;
                    if (mediaItem != null)
                    {
                        byte[] content = await mediaItem.GetContentAsync();
                        if (content != null)
                        {
                            string mediaFilePath = Path.Combine(_syncFolderPath, MEDIA_FOLDER, mediaItem.Id);
                            await File.WriteAllBytesAsync(mediaFilePath, content);
                        }
                    }
                }
            }

            // Update the manifest file to track the last sync time
            string manifestFilePath = Path.Combine(_syncFolderPath, MANIFEST_FILE);
            var manifest = new
            {
                LastSync = DateTime.Now,
                DeviceId = DeviceInfo.Name,
                ChangeCount = changes.Count
            };
            
            string manifestJson = JsonSerializer.Serialize(manifest);
            await File.WriteAllTextAsync(manifestFilePath, manifestJson);
        }

        public Task<List<string>> DetectDevicesAsync()
        {
            // On mobile this would scan available storage devices
            // For this simplified implementation, we'll just return a list of potential sync folders
            
            var devices = new List<string>();
            
            // Get external storage paths (simulated for this example)
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // On Android, we could check for mounted storage devices
                devices.Add("/storage/emulated/0/NotesSync");
                devices.Add("/storage/sdcard/NotesSync");
            }
            else if (DeviceInfo.Platform == DevicePlatform.iOS)
            {
                // On iOS, external storage options are limited
                devices.Add(Path.Combine(FileSystem.CacheDirectory, "NotesSync"));
            }
            else
            {
                // On other platforms, just use a folder in the app directory
                devices.Add(Path.Combine(FileSystem.AppDataDirectory, "NotesSync"));
            }
            
            return Task.FromResult(devices);
        }
    }
}