using System.Text;
using System.Text.Json;
using Notes.Data.Repositories;
using Notes.Data.Storage;
using Notes.Models;
using Notes.Services.Crypto;
using Notes.Services.Notes;
using Notes.Services;

namespace Notes.Services.Sync;

public class ReactiveSyncService : IDisposable
{
  private readonly NoteManager _noteManager;
  private readonly FolderManager _folderManager;
  private readonly MediaManager _mediaManager;
  private readonly NoteRepository _noteRepo;
  private readonly FolderRepository _folderRepo;
  private readonly SyncSettingsService _settingsService;
  private readonly SyncManager _syncManager;
  private readonly ToastService _toastService;
  private readonly ProgressNotificationService _progressService;

  private SyncApiClient? _client;
  private byte[]? _syncKey;
  private string? _deviceId;
  private CancellationTokenSource? _cts;
  private Task? _sseTask;
  private Task? _periodicTask;

  private readonly Dictionary<string, CancellationTokenSource> _pendingPush = new();
  private readonly SemaphoreSlim _pushLock = new(1, 1);

  // Fires on the UI thread when remote changes are applied, so pages can refresh.
  public event Action? RemoteChangesApplied;

  public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
  };

  private const string TimeFmt = "yyyy-MM-ddTHH:mm:ssZ";
  private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(5);
  private static readonly SyncProfile DefaultProfile = new() { Name = "Network", Protocol = SyncProtocolType.Network };

  public ReactiveSyncService(NoteManager noteManager, FolderManager folderManager,
      MediaManager mediaManager, NoteRepository noteRepo, FolderRepository folderRepo,
      SyncSettingsService settingsService, SyncManager syncManager, ToastService toastService,
      ProgressNotificationService progressService)
  {
    _noteManager = noteManager;
    _folderManager = folderManager;
    _mediaManager = mediaManager;
    _noteRepo = noteRepo;
    _folderRepo = folderRepo;
    _settingsService = settingsService;
    _syncManager = syncManager;
    _toastService = toastService;
    _progressService = progressService;

    _noteManager.NoteChanged += OnNoteChanged;
    _folderManager.FolderChanged += OnFolderChanged;
    _mediaManager.MediaAdded += OnMediaAdded;
  }

  public async Task StartAsync()
  {
    // Always stop first so settings changes take effect immediately.
    await StopAsync();

    var settings = await _settingsService.LoadAsync();
    if (!settings.Enabled
        || string.IsNullOrEmpty(settings.ServerUrl)
        || string.IsNullOrEmpty(settings.ApiToken))
      return;

    _deviceId = settings.DeviceId;
    _syncKey = SyncCryptoHelper.DeriveKeyFromToken(settings.ApiToken);
    _client = new SyncApiClient(settings.ServerUrl, settings.ApiToken);
    _cts = new CancellationTokenSource();
    DebugLogService.Current?.Log($"sync-start: url={settings.ServerUrl} device={_deviceId}");
    _sseTask = RunSseLoopAsync(_cts.Token);
    _periodicTask = RunPeriodicSyncAsync(_cts.Token);
  }

  public Task RestartAsync() => StartAsync();

  public async Task StopAsync()
  {
    _cts?.Cancel();
    var running = new[] { _sseTask, _periodicTask }.Where(t => t != null).Cast<Task>().ToArray();
    if (running.Length > 0)
      await Task.WhenAny(Task.WhenAll(running), Task.Delay(3000));
    _client?.Dispose();
    _client = null;
    _cts = null;
    _sseTask = null;
    _periodicTask = null;
  }

  // ── Periodic fallback sync ────────────────────────────────────────────────

  private async Task RunPeriodicSyncAsync(CancellationToken ct)
  {
    // Run an immediate sync on start so any uploads that were cut short (app kill,
    // StopAsync mid-queue) are recovered without waiting for the first 5-minute tick.
    if (!ct.IsCancellationRequested)
    {
      using var session = _progressService.Begin("Syncing");
      try
      {
        DebugLogService.Current?.Log("initial-sync-start");
        await _syncManager.SynchronizeAsync(DefaultProfile, session.Report);
        DebugLogService.Current?.Log("initial-sync-done");
        MainThread.BeginInvokeOnMainThread(() => RemoteChangesApplied?.Invoke());
      }
      catch (OperationCanceledException) { return; }
      catch (Exception ex) { DebugLogService.Current?.Log($"initial-sync-err: {ex.GetType().Name}: {ex.Message}"); }
    }

    using var timer = new PeriodicTimer(PeriodicInterval);
    while (await timer.WaitForNextTickAsync(ct))
    {
      using var session = _progressService.Begin("Syncing");
      try
      {
        DebugLogService.Current?.Log("periodic-sync-start");
        await _syncManager.SynchronizeAsync(DefaultProfile, session.Report);
        DebugLogService.Current?.Log("periodic-sync-done");
        MainThread.BeginInvokeOnMainThread(() => RemoteChangesApplied?.Invoke());
      }
      catch (Exception ex) { DebugLogService.Current?.Log($"periodic-sync-err: {ex.GetType().Name}: {ex.Message}"); }
    }
  }

  // ── SSE listener ──────────────────────────────────────────────────────────

  private async Task RunSseLoopAsync(CancellationToken ct)
  {
    var delay = TimeSpan.FromSeconds(2);
    while (!ct.IsCancellationRequested)
    {
      try
      {
        if (_client != null && _deviceId != null)
        {
          delay = TimeSpan.FromSeconds(2);
          await _client.SubscribeToEventsAsync(_deviceId, HandleSseEvent, ct);
        }
      }
      catch (OperationCanceledException) { break; }
      catch (Exception ex)
      {
        DebugLogService.Current?.Log($"sse-err: {ex.GetType().Name}: {ex.Message} reconnect={delay.TotalSeconds}s");
      }

      if (ct.IsCancellationRequested) break;
      try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
      delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
    }
  }

  private void HandleSseEvent(SseEvent evt)
  {
    if (evt.Type == "changes")
      _ = ApplyRemoteChangesAsync(evt);
  }

  private async Task ApplyRemoteChangesAsync(SseEvent evt)
  {
    // Capture locals — _client/_syncKey can be set to null by StopAsync on another thread.
    var client = _client;
    var key = _syncKey;
    if (client == null || key == null) return;
    try
    {
      var noteIds = evt.Notes ?? new List<string>();
      var folderIds = evt.Folders ?? new List<string>();

      var pull = await client.PullChangesAsync(noteIds, folderIds, new List<string>());
      if (pull == null) return;

      foreach (var item in pull.Notes)
      {
        try
        {
          byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), key);
          var note = JsonSerializer.Deserialize<Note>(Encoding.UTF8.GetString(dec), JsonOpts);
          if (note != null) await _noteRepo.SaveNoteSyncAsync(note);
        }
        catch { }
      }

      foreach (var item in pull.Folders)
      {
        try
        {
          byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), key);
          var folder = JsonSerializer.Deserialize<Folder>(Encoding.UTF8.GetString(dec), JsonOpts);
          if (folder != null) await _folderRepo.SaveFolderSyncAsync(folder);
        }
        catch { }
      }

      foreach (var id in (evt.DeletedNotes ?? new List<string>()))
        try { await _noteRepo.DeleteNoteAsync(id, createTombstone: false); } catch { }

      foreach (var id in (evt.DeletedFolders ?? new List<string>()))
        try { await _folderRepo.DeleteFolderAsync(id, createTombstone: false); } catch { }

      int deletedNotes = evt.DeletedNotes?.Count ?? 0;
      int deletedFolders = evt.DeletedFolders?.Count ?? 0;
      if (pull.Notes.Count > 0 || pull.Folders.Count > 0 || deletedNotes > 0 || deletedFolders > 0)
      {
        MainThread.BeginInvokeOnMainThread(() => RemoteChangesApplied?.Invoke());
        _toastService.Show(BuildSyncMessage(pull.Notes.Count, pull.Folders.Count, deletedNotes, deletedFolders));
      }
    }
    catch { }
  }

  // ── Local change handlers ─────────────────────────────────────────────────

  private void OnNoteChanged(string noteId, EntityChangeKind kind)
    => _ = SchedulePushAsync("n:" + noteId, () => PushNoteAsync(noteId, kind));

  private void OnFolderChanged(string folderId, EntityChangeKind kind)
    => _ = SchedulePushAsync("f:" + folderId, () => PushFolderAsync(folderId, kind));

  private void OnMediaAdded(string mediaId)
    => _ = SchedulePushAsync("m:" + mediaId, () => PushMediaAsync(mediaId));

  // Debounce: cancel previous pending push for the same entity, wait 400 ms, then push.
  private async Task SchedulePushAsync(string key, Func<Task> pushAction)
  {
    CancellationTokenSource cts;
    lock (_pendingPush)
    {
      if (_pendingPush.TryGetValue(key, out var prev))
        prev.Cancel();
      cts = new CancellationTokenSource();
      _pendingPush[key] = cts;
    }

    try { await Task.Delay(400, cts.Token).ConfigureAwait(false); }
    catch (OperationCanceledException) { return; }

    lock (_pendingPush) _pendingPush.Remove(key);

    await pushAction();
  }

  private async Task PushNoteAsync(string noteId, EntityChangeKind kind)
  {
    var ct = _cts?.Token ?? CancellationToken.None;
    try { await _pushLock.WaitAsync(ct); }
    catch (OperationCanceledException) { return; }
    try
    {
      var client = _client;
      var key = _syncKey;
      var deviceId = _deviceId;
      if (client == null || key == null || deviceId == null) return;
      if (kind == EntityChangeKind.Deleted)
      {
        await client.PushChangesAsync(new(), new(), new(), new List<string> { noteId }, new(), deviceId);
        return;
      }
      var note = await _noteRepo.GetNoteAsync(noteId);
      if (note == null) return;
      byte[] enc = SyncCryptoHelper.AesEncrypt(
          Encoding.UTF8.GetBytes(JsonSerializer.Serialize(note, JsonOpts)), key);
      var item = new SyncItem
      {
        Id = noteId,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = note.Modified.ToUniversalTime().ToString(TimeFmt),
      };
      await client.PushChangesAsync(new List<SyncItem> { item }, new(), new(), new(), new(), deviceId);
    }
    catch { }
    finally { _pushLock.Release(); }
  }

  private async Task PushFolderAsync(string folderId, EntityChangeKind kind)
  {
    var ct = _cts?.Token ?? CancellationToken.None;
    try { await _pushLock.WaitAsync(ct); }
    catch (OperationCanceledException) { return; }
    try
    {
      var client = _client;
      var key = _syncKey;
      var deviceId = _deviceId;
      if (client == null || key == null || deviceId == null) return;
      if (kind == EntityChangeKind.Deleted)
      {
        await client.PushChangesAsync(new(), new(), new(), new(), new List<string> { folderId }, deviceId);
        return;
      }
      var folder = await _folderRepo.GetFolderAsync(folderId);
      if (folder == null) return;
      byte[] enc = SyncCryptoHelper.AesEncrypt(
          Encoding.UTF8.GetBytes(JsonSerializer.Serialize(folder, JsonOpts)), key);
      var item = new SyncItem
      {
        Id = folderId,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = folder.Modified.ToUniversalTime().ToString(TimeFmt),
      };
      await client.PushChangesAsync(new(), new List<SyncItem> { item }, new(), new(), new(), deviceId);
    }
    catch { }
    finally { _pushLock.Release(); }
  }

  private async Task PushMediaAsync(string mediaId)
  {
    var ct = _cts?.Token ?? CancellationToken.None;
    try { await _pushLock.WaitAsync(ct); }
    catch (OperationCanceledException)
    {
      DebugLogService.Current?.Log($"media-push-cancel: id={mediaId}");
      return;
    }
    try
    {
      // Re-read after acquiring lock: StopAsync may have disposed _client while we waited.
      var client = _client;
      var key = _syncKey;
      var deviceId = _deviceId;
      if (client == null || key == null || deviceId == null)
      {
        DebugLogService.Current?.Log($"media-push-skip: id={mediaId} no client/key/device");
        return;
      }

      DebugLogService.Current?.Log($"media-push: id={mediaId}");
      var item = await _mediaManager.GetMediaAsync(mediaId);
      if (item == null) { DebugLogService.Current?.Log($"media-push-skip: id={mediaId} item not found"); return; }

      using var contentStream = await _mediaManager.GetMediaContentAsync(mediaId);
      using var ms = new MemoryStream();
      await contentStream.CopyToAsync(ms);

      var payload = new MediaSyncPayload
      {
        Metadata = item,
        ContentBase64 = Convert.ToBase64String(ms.ToArray()),
      };
      byte[] enc = SyncCryptoHelper.AesEncrypt(
          Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts)), key);
      var syncItem = new SyncItem
      {
        Id = mediaId,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = item.Created.ToUniversalTime().ToString(TimeFmt),
      };
      DebugLogService.Current?.Log($"media-push-enc: id={mediaId} rawBytes={ms.Length} encChars={syncItem.EncryptedData.Length}");
      await client.PushChunkedAsync(syncItem, "media", deviceId);
      DebugLogService.Current?.Log($"media-push-done: id={mediaId}");
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"media-push-err: id={mediaId} {ex.GetType().Name}: {ex.Message}");
    }
    finally { _pushLock.Release(); }
  }

  private static string BuildSyncMessage(int notes, int folders, int deletedNotes, int deletedFolders)
  {
    var parts = new List<string>();

    var received = new List<string>();
    if (notes > 0) received.Add($"{notes} {(notes == 1 ? "note" : "notes")}");
    if (folders > 0) received.Add($"{folders} {(folders == 1 ? "folder" : "folders")}");
    if (received.Count > 0)
      parts.Add("Received: " + string.Join(", ", received));

    var deleted = new List<string>();
    if (deletedNotes > 0) deleted.Add($"{deletedNotes} {(deletedNotes == 1 ? "note" : "notes")}");
    if (deletedFolders > 0) deleted.Add($"{deletedFolders} {(deletedFolders == 1 ? "folder" : "folders")}");
    if (deleted.Count > 0)
      parts.Add("Deleted: " + string.Join(", ", deleted));

    return string.Join(" · ", parts);
  }

  public void Dispose()
  {
    _noteManager.NoteChanged -= OnNoteChanged;
    _folderManager.FolderChanged -= OnFolderChanged;
    _mediaManager.MediaAdded -= OnMediaAdded;
    _cts?.Cancel();
    _client?.Dispose();
  }
}
