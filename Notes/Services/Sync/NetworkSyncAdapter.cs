using Notes.Models;

namespace Notes.Services.Sync;

public class NetworkSyncAdapter : ISyncAdapter
{
  public SyncProtocolType ProtocolType => SyncProtocolType.Network;
  public bool IsConnected { get; private set; }

  public async Task<bool> ConnectAsync(SyncProfile profile)
  {
    if (!profile.Settings.TryGetValue("ServerUrl", out var serverUrl))
      return false;

    IsConnected = true;
    return await Task.FromResult(true);
  }

  public async Task DisconnectAsync()
  {
    IsConnected = false;
    await Task.CompletedTask;
  }

  public async Task<List<SyncChange>> GetChangesAsync()
  {
    return await Task.FromResult(new List<SyncChange>());
  }

  public async Task ApplyChangesAsync(List<SyncChange> changes)
  {
    await Task.CompletedTask;
  }

  public bool TestConnection()
  {
    return true;
  }
}