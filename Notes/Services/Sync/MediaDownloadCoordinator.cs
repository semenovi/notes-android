using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Notes.Data.Storage;
using Notes.Services.Crypto;
using Notes.Services;

namespace Notes.Services.Sync;

public class MediaDownloadCoordinator : IDisposable
{
  private const int MaxRetriesPerCycle = 3;
  private static readonly TimeSpan BackgroundIdlePoll = TimeSpan.FromSeconds(3);

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
  };

  private readonly MediaStorage _mediaStorage;
  private readonly ProgressNotificationService _progressService;

  private SyncApiClient? _client;
  private byte[]? _key;
  private CancellationTokenSource? _loopCts;
  private Task? _loopTask;

  private readonly object _stateLock = new();
  private readonly List<string> _backgroundQueue = new();
  private readonly HashSet<string> _queuedIds = new();
  private readonly Dictionary<string, int> _retryCounts = new();
  private readonly HashSet<string> _givenUp = new();
  private HashSet<string> _noteFocusMediaIds = new();
  private HashSet<string> _folderFocusMediaIds = new();

  private readonly ConcurrentDictionary<string, Task<bool>> _inFlight = new();

  private readonly object _progressLock = new();
  private ProgressSession? _progressSession;
  private int _sessionTotal;
  private int _sessionDone;

  public event Action<string>? MediaAvailable;

  public MediaDownloadCoordinator(MediaStorage mediaStorage, ProgressNotificationService progressService)
  {
    _mediaStorage = mediaStorage;
    _progressService = progressService;
  }

  public void Configure(string serverUrl, string apiToken)
  {
    _client?.Dispose();
    _client = new SyncApiClient(serverUrl, apiToken);
    _key = SyncCryptoHelper.DeriveKeyFromToken(apiToken);
    DebugLogService.Current?.Log($"media-coordinator-configure: url={serverUrl}");
  }

  public void Start()
  {

    StopLoop();
    if (_client == null || _key == null)
    {
      DebugLogService.Current?.Log("media-coordinator-start-skip: not configured");
      return;
    }
    _loopCts = new CancellationTokenSource();
    _loopTask = RunBackgroundLoopAsync(_loopCts.Token);
    DebugLogService.Current?.Log("media-coordinator-start: loop launched");
  }

  private void StopLoop()
  {
    _loopCts?.Cancel();
    _loopCts = null;
    _loopTask = null;

    ClearQueueAndProgress();
  }

  private void ClearQueueAndProgress()
  {
    lock (_stateLock)
    {
      _backgroundQueue.Clear();
      _queuedIds.Clear();
      _retryCounts.Clear();
      _givenUp.Clear();
    }
    lock (_progressLock)
    {
      _progressSession?.Dispose();
      _progressSession = null;
      _sessionTotal = 0;
      _sessionDone = 0;
    }
  }

  public void Stop()
  {
    StopLoop();
    _client?.Dispose();
    _client = null;
    _key = null;
    DebugLogService.Current?.Log("media-coordinator-stop");
  }

  public void EnqueueBackground(IEnumerable<string> mediaIds)
  {
    var ids = mediaIds as IReadOnlyCollection<string> ?? mediaIds.ToList();
    int added = 0;
    lock (_stateLock)
    {
      foreach (var id in ids)
      {
        _givenUp.Remove(id);
        if (_queuedIds.Add(id))
        {
          _backgroundQueue.Add(id);
          added++;
        }
      }
    }
    DebugLogService.Current?.Log($"media-coordinator-enqueue: received={ids.Count} newlyAdded={added}");
    if (added > 0)
      OnItemsEnqueued(added);
  }

  public int PendingCount
  {
    get { lock (_stateLock) return _backgroundQueue.Count; }
  }

  public async Task DrainPendingAsync(string serverUrl, string apiToken, TimeSpan budget, CancellationToken ct)
  {
    if (_client == null || _key == null)
      Configure(serverUrl, apiToken);

    var deadline = DateTime.UtcNow + budget;
    while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
    {
      string? next = PickNextBackgroundId();
      if (next == null) return;

      bool ok;
      try
      {
        var task = _inFlight.GetOrAdd(next, id => FetchOneAsync(id));
        ok = await task.WaitAsync(ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) { return; }
      catch (Exception ex)
      {
        DebugLogService.Current?.Log($"media-drain-threw: id={next} {ex.GetType().Name}: {ex.Message}");
        ok = false;
      }

      if (ok)
      {
        lock (_stateLock) { _backgroundQueue.Remove(next); _queuedIds.Remove(next); }
        OnItemProcessed();
      }
      else
      {
        int retries = IncrementRetry(next);
        if (retries >= MaxRetriesPerCycle)
        {
          lock (_stateLock)
          {
            _backgroundQueue.Remove(next);
            _queuedIds.Remove(next);
            _retryCounts.Remove(next);
            _givenUp.Add(next);
          }
          OnItemProcessed();
        }
        else
        {
          try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 2 << retries)), ct).ConfigureAwait(false); }
          catch (OperationCanceledException) { return; }
        }
      }
    }
  }

  private void OnItemsEnqueued(int added)
  {
    lock (_progressLock)
    {
      DropDeadSessionLocked();
      bool isNewSession = _progressSession == null;
      _sessionTotal += added;
      _progressSession ??= _progressService.Begin(
          "downloading media", maxDurationMs: 0, idleTimeoutMs: 60_000);

      ReportProgressLocked(force: isNewSession);
    }
  }

  private void DropDeadSessionLocked()
  {
    if (_progressSession is { IsActive: false })
    {
      _progressSession = null;
      _sessionTotal = 0;
      _sessionDone = 0;
    }
  }

  private void OnItemProcessed()
  {
    lock (_progressLock)
    {
      DropDeadSessionLocked();
      if (_progressSession == null) return;
      _sessionDone++;
      if (_sessionDone >= _sessionTotal)
      {
        _progressSession.Dispose();
        _progressSession = null;
        _sessionTotal = 0;
        _sessionDone = 0;
      }
      else
      {
        ReportProgressLocked();
      }
    }
  }

  private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(200);
  private DateTime _lastProgressReportUtc = DateTime.MinValue;

  private void ReportProgressLocked(bool force = false)
  {
    DropDeadSessionLocked();
    if (_progressSession == null) return;
    var now = DateTime.UtcNow;
    if (!force && now - _lastProgressReportUtc < ProgressReportInterval) return;
    _lastProgressReportUtc = now;
    double fraction = _sessionTotal > 0 ? (double)_sessionDone / _sessionTotal : 0;
    string? subtitle = _sessionTotal > 1 ? $"{_sessionDone} of {_sessionTotal}" : null;
    _progressSession.Report(fraction, subtitle);
  }

  public void SetFolderFocus(IEnumerable<string> mediaIds)
  {
    lock (_stateLock) _folderFocusMediaIds = new HashSet<string>(mediaIds);
  }

  public void ClearFolderFocus()
  {
    lock (_stateLock) _folderFocusMediaIds = new HashSet<string>();
  }

  public void SetNoteFocus(IEnumerable<string> mediaIds)
  {
    lock (_stateLock) _noteFocusMediaIds = new HashSet<string>(mediaIds);
  }

  public void ClearNoteFocus()
  {
    lock (_stateLock) _noteFocusMediaIds = new HashSet<string>();
  }

  public async Task<bool> EnsureAvailableAsync(string mediaId, CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(mediaId)) return false;
    if (await _mediaStorage.HasLocalContentAsync(mediaId)) return true;
    if (_client == null || _key == null) return false;

    var task = _inFlight.GetOrAdd(mediaId, id => FetchOneAsync(id));
    try
    {
      return await task.WaitAsync(ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {

      return false;
    }
  }

  private async Task RunBackgroundLoopAsync(CancellationToken ct)
  {
    DebugLogService.Current?.Log("media-loop-enter");
    try
    {
      while (!ct.IsCancellationRequested)
      {
        string? next = PickNextBackgroundId();
        if (next == null)
        {
          try { await Task.Delay(BackgroundIdlePoll, ct); } catch (OperationCanceledException) { break; }
          continue;
        }

        int queueCount, retriesSoFar;
        lock (_stateLock) { queueCount = _backgroundQueue.Count; _retryCounts.TryGetValue(next, out retriesSoFar); }
        DebugLogService.Current?.Log($"media-loop-pick: id={next} queue={queueCount} priorRetries={retriesSoFar}");

        var task = _inFlight.GetOrAdd(next, id => FetchOneAsync(id));
        bool ok;
        try { ok = await task; }
        catch (Exception ex)
        {
          DebugLogService.Current?.Log($"media-loop-fetch-threw: id={next} {ex.GetType().Name}: {ex.Message}");
          ok = false;
        }

        if (ok)
        {
          lock (_stateLock) { _backgroundQueue.Remove(next); _queuedIds.Remove(next); }
          OnItemProcessed();
          DebugLogService.Current?.Log($"media-loop-ok: id={next}");
        }
        else
        {
          int retries = IncrementRetry(next);
          if (retries >= MaxRetriesPerCycle)
          {
            lock (_stateLock)
            {
              _backgroundQueue.Remove(next);
              _queuedIds.Remove(next);
              _retryCounts.Remove(next);
              _givenUp.Add(next);
            }
            OnItemProcessed();
            DebugLogService.Current?.Log($"media-loop-give-up: id={next} retries={retries}");
          }
          else
          {
            DebugLogService.Current?.Log($"media-loop-retry-wait: id={next} retries={retries}");

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << retries)), ct); }
            catch (OperationCanceledException) { break; }
          }
        }
      }
    }
    catch (Exception ex)
    {

      DebugLogService.Current?.Log($"media-loop-crashed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
    DebugLogService.Current?.Log("media-loop-exit");
  }

  private int IncrementRetry(string mediaId)
  {
    lock (_stateLock)
    {
      _retryCounts.TryGetValue(mediaId, out int count);
      count++;
      _retryCounts[mediaId] = count;
      return count;
    }
  }

  private string? PickNextBackgroundId()
  {
    lock (_stateLock)
    {
      if (_backgroundQueue.Count == 0) return null;

      foreach (var id in _backgroundQueue)
        if (_noteFocusMediaIds.Contains(id)) return id;
      foreach (var id in _backgroundQueue)
        if (_folderFocusMediaIds.Contains(id)) return id;
      return _backgroundQueue[0];
    }
  }

  private async Task<bool> FetchOneAsync(string mediaId)
  {
    try
    {
      var client = _client;
      var key = _key;
      if (client == null || key == null)
      {
        DebugLogService.Current?.Log($"media-fetch-skip: id={mediaId} not configured");
        return false;
      }

      if (await _mediaStorage.HasLocalContentAsync(mediaId))
      {
        DebugLogService.Current?.Log($"media-fetch-already-local: id={mediaId}");
        return true;
      }

      DebugLogService.Current?.Log($"media-fetch-pull: id={mediaId}");
      var pull = await client.PullChangesAsync(new(), new(), new List<string> { mediaId });
      var item = pull?.Media?.FirstOrDefault();
      if (item == null)
      {
        DebugLogService.Current?.Log($"media-fetch-empty-response: id={mediaId} pullNull={pull == null}");
        return false;
      }

      byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), key);
      var payload = JsonSerializer.Deserialize<MediaSyncPayload>(Encoding.UTF8.GetString(dec), JsonOpts);
      if (payload?.Metadata == null || string.IsNullOrEmpty(payload.ContentBase64))
      {
        DebugLogService.Current?.Log($"media-fetch-bad-payload: id={mediaId}");
        return false;
      }

      byte[] content = Convert.FromBase64String(payload.ContentBase64);
      await _mediaStorage.SaveMediaFromSyncAsync(payload.Metadata, content);
      DebugLogService.Current?.Log($"media-fetch-done: id={mediaId} bytes={content.Length}");
      MediaAvailable?.Invoke(mediaId);
      return true;
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"media-fetch-err: id={mediaId} {ex.GetType().Name}: {ex.Message}");
      return false;
    }
    finally
    {
      _inFlight.TryRemove(mediaId, out _);
    }
  }

  public void Dispose()
  {
    Stop();
  }
}
