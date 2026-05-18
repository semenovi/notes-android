using System.Text.Json;
using Notes.Data.Repositories;
using Notes.Data.Storage;
using Notes.Models;

namespace Notes.Services.Sync;

public class SyncManager
{
  private readonly List<ISyncAdapter> _adapters;
  private readonly NoteRepository _noteRepo;
  private readonly FolderRepository _folderRepo;
  private readonly MediaStorage _mediaStorage;

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
  };

  public SyncManager(IEnumerable<ISyncAdapter> adapters, NoteRepository noteRepo,
      FolderRepository folderRepo, MediaStorage mediaStorage)
  {
    _adapters = adapters.ToList();
    _noteRepo = noteRepo;
    _folderRepo = folderRepo;
    _mediaStorage = mediaStorage;
  }

  public async Task SynchronizeAsync(SyncProfile profile)
  {
    ISyncAdapter? adapter = _adapters.FirstOrDefault(a => a.ProtocolType == profile.Protocol);
    if (adapter == null)
      throw new InvalidOperationException($"No adapter registered for protocol {profile.Protocol}");

    if (!await adapter.ConnectAsync(profile))
      throw new InvalidOperationException("Failed to connect with the sync adapter");

    try
    {
      List<SyncChange> remoteChanges = await adapter.GetChangesAsync();
      await ApplyRemoteChangesAsync(remoteChanges);

      List<SyncChange> localChanges = await GetLocalChangesAsync();
      await adapter.ApplyChangesAsync(localChanges);
    }
    finally
    {
      await adapter.DisconnectAsync();
    }
  }

  private async Task<List<SyncChange>> GetLocalChangesAsync()
  {
    var changes = new List<SyncChange>();
    const string FMT = "yyyy-MM-ddTHH:mm:ssZ";

    var notes = await _noteRepo.GetAllNotesAsync();
    foreach (var note in notes)
    {
      changes.Add(new SyncChange
      {
        Id = note.Id,
        EntityType = SyncEntityType.Note,
        ChangeType = SyncChangeType.Update,
        Data = JsonSerializer.Serialize(note, JsonOpts),
        Timestamp = note.Modified,
      });
    }

    var folders = await _folderRepo.GetAllFoldersAsync();
    foreach (var folder in folders)
    {
      changes.Add(new SyncChange
      {
        Id = folder.Id,
        EntityType = SyncEntityType.Folder,
        ChangeType = SyncChangeType.Update,
        Data = JsonSerializer.Serialize(folder, JsonOpts),
        Timestamp = folder.Modified,
      });
    }

    var mediaItems = await _mediaStorage.GetAllMediaAsync();
    foreach (var item in mediaItems)
    {
      try
      {
        byte[] content = await _mediaStorage.GetRawContentAsync(item.Id);
        var payload = new MediaSyncPayload
        {
          Metadata = item,
          ContentBase64 = Convert.ToBase64String(content),
        };
        changes.Add(new SyncChange
        {
          Id = item.Id,
          EntityType = SyncEntityType.Media,
          ChangeType = SyncChangeType.Update,
          Data = JsonSerializer.Serialize(payload, JsonOpts),
          Timestamp = item.Created,
        });
      }
      catch { }
    }

    return changes;
  }

  private async Task ApplyRemoteChangesAsync(List<SyncChange> changes)
  {
    foreach (var change in changes)
    {
      try
      {
        switch (change.EntityType)
        {
          case SyncEntityType.Note:
            await ApplyNoteChangeAsync(change);
            break;
          case SyncEntityType.Folder:
            await ApplyFolderChangeAsync(change);
            break;
          case SyncEntityType.Media:
            await ApplyMediaChangeAsync(change);
            break;
        }
      }
      catch { }
    }
  }

  private async Task ApplyNoteChangeAsync(SyncChange change)
  {
    if (change.ChangeType == SyncChangeType.Delete)
    {
      await _noteRepo.DeleteNoteAsync(change.Id, createTombstone: false);
      return;
    }
    var note = JsonSerializer.Deserialize<Note>(change.Data, JsonOpts);
    if (note != null)
      await _noteRepo.SaveNoteSyncAsync(note);
  }

  private async Task ApplyFolderChangeAsync(SyncChange change)
  {
    if (change.ChangeType == SyncChangeType.Delete)
    {
      await _folderRepo.DeleteFolderAsync(change.Id, createTombstone: false);
      return;
    }
    var folder = JsonSerializer.Deserialize<Folder>(change.Data, JsonOpts);
    if (folder != null)
      await _folderRepo.SaveFolderSyncAsync(folder);
  }

  private async Task ApplyMediaChangeAsync(SyncChange change)
  {
    var payload = JsonSerializer.Deserialize<MediaSyncPayload>(change.Data, JsonOpts);
    if (payload?.Metadata == null || string.IsNullOrEmpty(payload.ContentBase64)) return;

    // Skip if we already have this media item locally
    var existing = await _mediaStorage.GetMediaAsync(payload.Metadata.Id);
    if (existing != null) return;

    byte[] content = Convert.FromBase64String(payload.ContentBase64);
    await _mediaStorage.SaveMediaFromSyncAsync(payload.Metadata, content);
  }
}

public class MediaSyncPayload
{
  public MediaItem Metadata { get; set; } = new();
  public string ContentBase64 { get; set; } = string.Empty;
}

public class SyncConflict
{
  public SyncChange RemoteChange { get; set; } = null!;
  public SyncChange LocalChange { get; set; } = null!;
  public SyncConflictResolution Resolution { get; set; } = SyncConflictResolution.None;
}

public enum SyncConflictResolution
{
  None,
  KeepRemote,
  KeepLocal,
  Merge,
}
