using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Notes.Models;

namespace Notes.Services.Sync
{
    public enum SyncItemType
    {
        Note,
        Folder,
        Media
    }

    public enum SyncChangeType
    {
        Added,
        Modified,
        Deleted
    }

    public enum SyncConflictType
    {
        Modified,
        Deleted
    }

    public class SyncChange
    {
        public string Id { get; set; }
        public SyncItemType ItemType { get; set; }
        public SyncChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; }
        public object Data { get; set; }
        public byte[] BinaryData { get; set; }
    }

    public class SyncConflict
    {
        public object LocalItem { get; set; }
        public SyncChange RemoteChange { get; set; }
        public SyncConflictType ConflictType { get; set; }
    }

    public abstract class SyncAdapter
    {
        public SyncProtocolType ProtocolType { get; protected set; }
        public bool IsConnected { get; protected set; }

        public abstract Task ConnectAsync(SyncProfile profile);
        public abstract Task DisconnectAsync();
        public abstract Task<List<SyncChange>> GetChangesAsync();
        public abstract Task ApplyChangesAsync(List<SyncChange> changes);
    }
}