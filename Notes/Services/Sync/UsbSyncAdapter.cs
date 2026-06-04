using Notes.Models;

namespace Notes.Services.Sync;

public class UsbSyncAdapter : ISyncAdapter
{
  public SyncProtocolType ProtocolType => SyncProtocolType.Usb;
  public bool IsConnected { get; private set; }

  public async Task<bool> ConnectAsync(SyncProfile profile)
  {
    IsConnected = true;
    return await Task.FromResult(true);
  }

  public async Task DisconnectAsync()
  {
    IsConnected = false;
    await Task.CompletedTask;
  }

  public async Task<List<SyncChange>> GetChangesAsync(Action<double, string?>? onProgress = null)
  {
    return await Task.FromResult(new List<SyncChange>());
  }

  public async Task ApplyChangesAsync(List<SyncChange> changes, Action<double, string?>? onProgress = null)
  {
    await Task.CompletedTask;
  }

  public List<string> DetectDevices()
  {
    return new List<string>();
  }
}