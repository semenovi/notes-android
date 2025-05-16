using Notes.Models;

namespace Notes.Services.Sync;

public class SyncManager
{
  private readonly List<ISyncAdapter> _adapters = new List<ISyncAdapter>();
  private readonly Dictionary<string, DateTime> _lastSyncTimes = new Dictionary<string, DateTime>();

  public void RegisterAdapter(ISyncAdapter adapter)
  {
    if (!_adapters.Any(a => a.ProtocolType == adapter.ProtocolType))
    {
      _adapters.Add(adapter);
    }
  }

  public async Task SynchronizeAsync(SyncProfile profile)
  {
    ISyncAdapter? adapter = _adapters.FirstOrDefault(a => a.ProtocolType == profile.Protocol);

    if (adapter == null)
      throw new InvalidOperationException($"No adapter registered for protocol {profile.Protocol}");

    bool connected = await adapter.ConnectAsync(profile);

    if (!connected)
      throw new InvalidOperationException("Failed to connect with the sync adapter");

    try
    {
      List<SyncChange> remoteChanges = await adapter.GetChangesAsync();
      List<SyncChange> localChanges = GetLocalChanges(profile.Id);

      List<SyncConflict> conflicts = DetectConflicts(remoteChanges, localChanges);

      if (conflicts.Any())
      {
        foreach (var conflict in conflicts)
        {
          await ResolveConflictAsync(conflict);
        }
      }

      List<SyncChange> changesToApply = remoteChanges.Where(rc =>
          !conflicts.Any(c => c.RemoteChange.Id == rc.Id)
      ).ToList();

      await ApplyRemoteChangesAsync(changesToApply);
      await adapter.ApplyChangesAsync(localChanges.Where(lc =>
          !conflicts.Any(c => c.LocalChange.Id == lc.Id)
      ).ToList());

      _lastSyncTimes[profile.Id] = DateTime.Now;
    }
    finally
    {
      await adapter.DisconnectAsync();
    }
  }

  public List<SyncConflict> DetectConflicts(List<SyncChange> remoteChanges, List<SyncChange> localChanges)
  {
    List<SyncConflict> conflicts = new List<SyncConflict>();

    foreach (var remoteChange in remoteChanges)
    {
      foreach (var localChange in localChanges)
      {
        if (remoteChange.Id == localChange.Id && remoteChange.EntityType == localChange.EntityType)
        {
          conflicts.Add(new SyncConflict
          {
            RemoteChange = remoteChange,
            LocalChange = localChange
          });
        }
      }
    }

    return conflicts;
  }

  public async Task ResolveConflictAsync(SyncConflict conflict)
  {
    if (conflict.LocalChange.Timestamp > conflict.RemoteChange.Timestamp)
    {
      conflict.Resolution = SyncConflictResolution.KeepLocal;
    }
    else
    {
      conflict.Resolution = SyncConflictResolution.KeepRemote;
    }

    await Task.CompletedTask;
  }

  private List<SyncChange> GetLocalChanges(string profileId)
  {
    DateTime lastSync = DateTime.MinValue;
    _lastSyncTimes.TryGetValue(profileId, out lastSync);

    var changes = new List<SyncChange>();

    return changes;
  }

  private async Task ApplyRemoteChangesAsync(List<SyncChange> changes)
  {
    foreach (var change in changes)
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
  }

  private async Task ApplyNoteChangeAsync(SyncChange change)
  {
    await Task.CompletedTask;
  }

  private async Task ApplyFolderChangeAsync(SyncChange change)
  {
    await Task.CompletedTask;
  }

  private async Task ApplyMediaChangeAsync(SyncChange change)
  {
    await Task.CompletedTask;
  }
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
  Merge
}