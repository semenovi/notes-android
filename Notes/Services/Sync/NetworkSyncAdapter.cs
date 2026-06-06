using System.Text;
using System.Text.Json;
using Notes.Data.Repositories;
using Notes.Data.Storage;
using Notes.Models;
using Notes.Services.Crypto;
using Notes.Services;

namespace Notes.Services.Sync;

public class NetworkSyncAdapter : ISyncAdapter
{
  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
  };

  private readonly NoteRepository _noteRepo;
  private readonly FolderRepository _folderRepo;
  private readonly MediaStorage _mediaStorage;
  private readonly SyncSettingsService _settingsService;

  public SyncProtocolType ProtocolType => SyncProtocolType.Network;
  public bool IsConnected { get; private set; }

  private SyncSettings? _settings;
  private byte[]? _syncKey;
  private SyncApiClient? _apiClient;
  private List<string> _toUploadNotes = new();
  private List<string> _toUploadFolders = new();
  private List<string> _toUploadMedia = new();

  public NetworkSyncAdapter(NoteRepository noteRepo, FolderRepository folderRepo,
      MediaStorage mediaStorage, SyncSettingsService settingsService)
  {
    _noteRepo = noteRepo;
    _folderRepo = folderRepo;
    _mediaStorage = mediaStorage;
    _settingsService = settingsService;
  }

  public async Task<bool> ConnectAsync(SyncProfile profile)
  {
    _settings = await _settingsService.LoadAsync();

    if (!_settings.Enabled
        || string.IsNullOrEmpty(_settings.ServerUrl)
        || string.IsNullOrEmpty(_settings.ApiToken))
      return false;

    _apiClient = new SyncApiClient(_settings.ServerUrl, _settings.ApiToken);

    // Key is derived from the shared API token — no key exchange needed
    _syncKey = SyncCryptoHelper.DeriveKeyFromToken(_settings.ApiToken);

    await _apiClient.RegisterDeviceAsync(_settings.DeviceId);

    IsConnected = true;
    return true;
  }

  public async Task<List<SyncChange>> GetChangesAsync(Action<double, string?>? onProgress = null)
  {
    if (_apiClient == null || _syncKey == null || _settings == null)
      return new List<SyncChange>();

    var allNotes = await _noteRepo.GetAllNotesAsync();
    var allFolders = await _folderRepo.GetAllFoldersAsync();
    var allMedia = await _mediaStorage.GetAllMediaAsync();
    var tombstoneNotes = await _noteRepo.GetDeletionTombstonesAsync();
    var tombstoneFolders = await _folderRepo.GetDeletionTombstonesAsync();
    var tombstoneMedia = await _mediaStorage.GetDeletionTombstonesAsync();

    const string FMT = "yyyy-MM-ddTHH:mm:ssZ";
    var manifestRequest = new SyncManifestRequest
    {
      Notes = allNotes.ToDictionary(n => n.Id, n => n.Modified.ToUniversalTime().ToString(FMT)),
      Folders = allFolders.ToDictionary(f => f.Id, f => f.Modified.ToUniversalTime().ToString(FMT)),
      Media = allMedia.ToDictionary(m => m.Id, m => m.Created.ToUniversalTime().ToString(FMT)),
      DeletedNotes = tombstoneNotes,
      DeletedFolders = tombstoneFolders,
      DeletedMedia = tombstoneMedia,
    };

    ManifestResponse? manifestResp = await _apiClient.PostManifestAsync(manifestRequest);
    if (manifestResp == null)
      throw new InvalidOperationException("Server did not respond to the manifest request. Check the URL and token.");

    foreach (var id in tombstoneNotes.Keys) await _noteRepo.ClearTombstoneAsync(id);
    foreach (var id in tombstoneFolders.Keys) await _folderRepo.ClearTombstoneAsync(id);
    foreach (var id in tombstoneMedia.Keys) await _mediaStorage.ClearTombstoneAsync(id);

    _toUploadNotes = manifestResp.ToUpload.Notes;
    _toUploadFolders = manifestResp.ToUpload.Folders;
    _toUploadMedia = manifestResp.ToUpload.Media;
    DebugLogService.Current?.Log($"manifest: ul(n={_toUploadNotes.Count} f={_toUploadFolders.Count} m={_toUploadMedia.Count}) dl(n={manifestResp.ToDownload.Notes.Count} f={manifestResp.ToDownload.Folders.Count} m={manifestResp.ToDownload.Media.Count})");

    // Notes and folders are small — pull in one batch.
    PullResponse? pull = await _apiClient.PullChangesAsync(
        manifestResp.ToDownload.Notes,
        manifestResp.ToDownload.Folders,
        new List<string>());
    if (pull == null)
      throw new InvalidOperationException("Server did not respond to the data pull request.");

    var changes = new List<SyncChange>();

    foreach (var item in pull.Notes)
    {
      try
      {
        byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), _syncKey);
        changes.Add(new SyncChange
        {
          Id = item.Id,
          EntityType = SyncEntityType.Note,
          ChangeType = SyncChangeType.Update,
          Data = Encoding.UTF8.GetString(dec),
          Timestamp = DateTime.Parse(item.Modified, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime(),
        });
      }
      catch { /* skip undecryptable item — will be overwritten on push */ }
    }

    foreach (var item in pull.Folders)
    {
      try
      {
        byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), _syncKey);
        changes.Add(new SyncChange
        {
          Id = item.Id,
          EntityType = SyncEntityType.Folder,
          ChangeType = SyncChangeType.Update,
          Data = Encoding.UTF8.GetString(dec),
          Timestamp = DateTime.Parse(item.Modified, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime(),
        });
      }
      catch { /* skip undecryptable item — will be overwritten on push */ }
    }

    // Media items are pulled one by one and saved immediately to avoid loading all images
    // into memory at once, which causes OOM crashes on Android with large photo libraries.
    var mediaToDownload = manifestResp.ToDownload.Media;
    int totalDl = mediaToDownload.Count;
    if (totalDl > 0)
      onProgress?.Invoke(0.0, totalDl > 1 ? $"Downloading media 1 of {totalDl}" : "Downloading media");
    for (int dlIdx = 0; dlIdx < totalDl; dlIdx++)
    {
      var mediaId = mediaToDownload[dlIdx];
      try
      {
        PullResponse? mediaPull = await _apiClient.PullChangesAsync(
            new List<string>(), new List<string>(), new List<string> { mediaId });
        if (mediaPull?.Media == null) continue;
        foreach (var item in mediaPull.Media)
        {
          try
          {
            byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), _syncKey);
            var payload = JsonSerializer.Deserialize<MediaSyncPayload>(Encoding.UTF8.GetString(dec), JsonOpts);
            if (payload?.Metadata == null || string.IsNullOrEmpty(payload.ContentBase64)) continue;
            var existing = await _mediaStorage.GetMediaAsync(payload.Metadata.Id);
            if (existing != null) continue;
            byte[] content = Convert.FromBase64String(payload.ContentBase64);
            await _mediaStorage.SaveMediaFromSyncAsync(payload.Metadata, content);
            DebugLogService.Current?.Log($"pull-media-done: id={mediaId}");
          }
          catch (Exception ex)
          {
            DebugLogService.Current?.Log($"pull-media-item-err: id={mediaId} {ex.GetType().Name}: {ex.Message}");
          }
        }
      }
      catch (Exception ex)
      {
        DebugLogService.Current?.Log($"pull-media-err: id={mediaId} {ex.GetType().Name}: {ex.Message}");
      }
      onProgress?.Invoke((double)(dlIdx + 1) / totalDl,
          totalDl > 1 ? $"Downloading media {dlIdx + 1} of {totalDl}" : "Downloading media");
    }

    foreach (var id in manifestResp.ToDeleteLocal.Notes)
      changes.Add(new SyncChange { Id = id, EntityType = SyncEntityType.Note, ChangeType = SyncChangeType.Delete, Timestamp = DateTime.UtcNow });

    foreach (var id in manifestResp.ToDeleteLocal.Folders)
      changes.Add(new SyncChange { Id = id, EntityType = SyncEntityType.Folder, ChangeType = SyncChangeType.Delete, Timestamp = DateTime.UtcNow });

    foreach (var id in manifestResp.ToDeleteLocal.Media)
      changes.Add(new SyncChange { Id = id, EntityType = SyncEntityType.Media, ChangeType = SyncChangeType.Delete, Timestamp = DateTime.UtcNow });

    return changes;
  }

  public async Task ApplyChangesAsync(List<SyncChange> localChanges, Action<double, string?>? onProgress = null)
  {
    if (_apiClient == null || _syncKey == null) return;

    const string FMT = "yyyy-MM-ddTHH:mm:ssZ";
    var syncNotes = new List<SyncItem>();
    var syncFolders = new List<SyncItem>();

    foreach (var c in localChanges.Where(c => c.EntityType == SyncEntityType.Note
        && c.ChangeType != SyncChangeType.Delete
        && _toUploadNotes.Contains(c.Id)))
    {
      byte[] enc = SyncCryptoHelper.AesEncrypt(Encoding.UTF8.GetBytes(c.Data), _syncKey);
      syncNotes.Add(new SyncItem
      {
        Id = c.Id,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = c.Timestamp.ToUniversalTime().ToString(FMT),
      });
    }

    foreach (var c in localChanges.Where(c => c.EntityType == SyncEntityType.Folder
        && c.ChangeType != SyncChangeType.Delete
        && _toUploadFolders.Contains(c.Id)))
    {
      byte[] enc = SyncCryptoHelper.AesEncrypt(Encoding.UTF8.GetBytes(c.Data), _syncKey);
      syncFolders.Add(new SyncItem
      {
        Id = c.Id,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = c.Timestamp.ToUniversalTime().ToString(FMT),
      });
    }

    // Notes and folders are small — send in one request via the push endpoint.
    if (syncNotes.Count > 0 || syncFolders.Count > 0)
    {
      DebugLogService.Current?.Log($"full-sync-push: notes={syncNotes.Count} folders={syncFolders.Count}");
      await _apiClient.PushChangesAsync(syncNotes, syncFolders, new(), new(), new(), _settings?.DeviceId);
    }

    // Media: load content only for items that actually need uploading (lazy — avoids loading all media into memory).
    var mediaToUpload = localChanges.Where(c => c.EntityType == SyncEntityType.Media
        && c.ChangeType != SyncChangeType.Delete
        && _toUploadMedia.Contains(c.Id)).ToList();
    int totalUl = mediaToUpload.Count;
    if (totalUl > 0)
      onProgress?.Invoke(0.0, totalUl > 1 ? $"Uploading media 1 of {totalUl}" : "Uploading media");
    for (int ulIdx = 0; ulIdx < totalUl; ulIdx++)
    {
      var c = mediaToUpload[ulIdx];
      try
      {
        var metadata = JsonSerializer.Deserialize<MediaItem>(c.Data, JsonOpts);
        if (metadata == null) { DebugLogService.Current?.Log($"full-sync-media-skip: id={c.Id} no metadata"); continue; }
        byte[] content = await _mediaStorage.GetRawContentAsync(c.Id);
        var payload = new MediaSyncPayload { Metadata = metadata, ContentBase64 = Convert.ToBase64String(content) };
        byte[] enc = SyncCryptoHelper.AesEncrypt(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts)), _syncKey);
        var syncItem = new SyncItem
        {
          Id = c.Id,
          EncryptedData = Convert.ToBase64String(enc),
          Modified = c.Timestamp.ToUniversalTime().ToString(FMT),
        };
        DebugLogService.Current?.Log($"full-sync-media: id={syncItem.Id} encChars={syncItem.EncryptedData.Length}");
        int captured = ulIdx;
        await _apiClient.PushChunkedAsync(syncItem, "media", _settings?.DeviceId,
            (sent, total) => onProgress?.Invoke((captured + (double)sent / total) / totalUl,
                totalUl > 1 ? $"Uploading media {captured + 1} of {totalUl}" : null));
        DebugLogService.Current?.Log($"full-sync-media-done: id={syncItem.Id}");
      }
      catch (Exception ex)
      {
        DebugLogService.Current?.Log($"full-sync-media-err: id={c.Id} {ex.GetType().Name}: {ex.Message}");
      }
    }
  }

  public async Task DisconnectAsync()
  {
    IsConnected = false;
    _apiClient?.Dispose();
    _apiClient = null;
    await Task.CompletedTask;
  }
}
