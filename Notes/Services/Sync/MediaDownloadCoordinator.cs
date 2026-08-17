using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Notes.Data.Storage;
using Notes.Services.Crypto;
using Notes.Services;

namespace Notes.Services.Sync;

// Owns background/on-demand fetching of media content, decoupled from the note/folder
// sync cycle. NetworkSyncAdapter hands off "known missing" ids after the manifest phase
// instead of downloading them inline, so the app never blocks showing notes/folders on
// media transfer. MarkdownProcessor calls EnsureAvailableAsync on demand when a note is
// actually opened and an image isn't local yet.
//
// Uses its own SyncApiClient/key rather than reusing NetworkSyncAdapter's — that adapter
// keeps per-sync state (_toUploadNotes etc.) in instance fields that must not be touched
// concurrently (see CLAUDE.md), and this coordinator's fetches run independently of the
// full-sync cycle, potentially at the same time.
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

  // Tracks the coordinator's own progress UI, spanning the whole background queue's
  // lifetime rather than any single sync call — the full sync itself now returns almost
  // instantly (notes/folders only), so the old "wrap SynchronizeAsync in a progress
  // session" approach no longer has anything long-running to attach to for media.
  private readonly object _progressLock = new();
  private ProgressSession? _progressSession;
  private int _sessionTotal;
  private int _sessionDone;

  // Fired after a media item is newly saved to local storage. Not marshalled to any
  // particular thread — subscribers that touch UI must dispatch themselves.
  public event Action<string>? MediaAvailable;

  public MediaDownloadCoordinator(MediaStorage mediaStorage, ProgressNotificationService progressService)
  {
    _mediaStorage = mediaStorage;
    _progressService = progressService;
  }

  // Called once per sync (re)configuration, mirroring ReactiveSyncService's own
  // settings reload, so both stay in sync about server url/token/enabled state.
  public void Configure(string serverUrl, string apiToken)
  {
    _client?.Dispose();
    _client = new SyncApiClient(serverUrl, apiToken);
    _key = SyncCryptoHelper.DeriveKeyFromToken(apiToken);
    DebugLogService.Current?.Log($"media-coordinator-configure: url={serverUrl}");
  }

  public void Start()
  {
    // Only stop a previously running loop — must NOT call the full Stop() below, which
    // also clears _client/_key: Configure() is always called right before Start(), and
    // Stop()-ing here would immediately null out the credentials Start() is about to
    // check, making the loop never actually start.
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
  }

  public void Stop()
  {
    StopLoop();
    _client?.Dispose();
    _client = null;
    _key = null;
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
    DebugLogService.Current?.Log("media-coordinator-stop");
  }

  // Ids the last manifest exchange reported as missing locally. Re-adding an id already
  // in the queue is a no-op; re-adding one that had exhausted retries gives it a fresh
  // budget, since a new manifest round means the server still thinks we need it.
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

  // Opens (or extends) the "downloading media" progress session covering everything
  // currently queued. Reported as a fraction of the whole batch queued since the
  // session opened, not just this call's items — a slow connection that's still
  // working through an earlier batch when a new manifest round adds more shouldn't
  // make the bar jump backwards or restart.
  private void OnItemsEnqueued(int added)
  {
    lock (_progressLock)
    {
      bool isNewSession = _progressSession == null;
      _sessionTotal += added;
      _progressSession ??= _progressService.Begin("downloading media");
      // Always show the very first report (0 of N) — only steady-state updates below
      // get throttled.
      ReportProgressLocked(force: isNewSession);
    }
  }

  // Called once per item that leaves the queue for good (fetched, or given up after
  // exhausting retries) — both count as "processed" for the purpose of the bar, so a
  // permanently unreachable item doesn't stall it short of 100%.
  private void OnItemProcessed()
  {
    lock (_progressLock)
    {
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

  // Every ProgressSession.Report() posts a MainThread.BeginInvokeOnMainThread call that
  // repaints the overlay (label + progress bar), which forces a layout pass. With
  // hundreds of small/already-cached items finishing within milliseconds of each other,
  // reporting on every single one was flooding the UI thread with repaints — that's the
  // stutter during a big catch-up download, not the debug logging. Throttled to a
  // steady rate here instead; the count of items itself is cheap and lock-only.
  private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(200);
  private DateTime _lastProgressReportUtc = DateTime.MinValue;

  // Caller holds _progressLock.
  private void ReportProgressLocked(bool force = false)
  {
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

  // Primary on-demand primitive: returns true once content is present locally, fetching
  // it right away (outside the background queue's ordering) if it isn't. Concurrent
  // callers for the same id — including the background loop picking up the same id —
  // join the same in-flight fetch instead of downloading it twice.
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
      // Caller gave up waiting — the fetch itself keeps running in the background and
      // will still land on disk (and fire MediaAvailable) for whoever asks next.
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
            // Leave it queued but don't hot-loop on the same failing item.
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << retries)), ct); }
            catch (OperationCanceledException) { break; }
          }
        }
      }
    }
    catch (Exception ex)
    {
      // Should be unreachable (every branch above already catches its own exceptions),
      // but if something new starts throwing, log it loudly instead of the loop task
      // just dying silently and the queue looking permanently stuck at "0 of N".
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
      // Priority: media of the currently open note > media of the currently open
      // folder's notes > everything else (FIFO within a tier).
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

      // Idempotency: content may have arrived via a different path (a concurrent
      // fetch, or a full sync that ran in between) while this call was queued.
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
