using Notes.Models;

namespace Notes.Services.Sync;

public interface ISyncAdapter
{
  SyncProtocolType ProtocolType { get; }
  Task<bool> ConnectAsync(SyncProfile profile);
  Task DisconnectAsync();
  bool IsConnected { get; }
  Task<List<SyncChange>> GetChangesAsync();
  Task ApplyChangesAsync(List<SyncChange> changes);
}

public class SyncChange
{
  public string Id { get; set; } = string.Empty;
  public SyncChangeType ChangeType { get; set; }
  public SyncEntityType EntityType { get; set; }
  public string Data { get; set; } = string.Empty;
  public DateTime Timestamp { get; set; }
}

public enum SyncChangeType
{
  Create,
  Update,
  Delete
}

public enum SyncEntityType
{
  Note,
  Folder,
  Media
}