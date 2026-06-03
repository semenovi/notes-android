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

  public async Task<List<SyncChange>> GetChangesAsync()
  {
    if (_apiClient == null || _syncKey == null || _settings == null)
      return new List<SyncChange>();

    var allNotes = await _noteRepo.GetAllNotesAsync();
    var allFolders = await _folderRepo.GetAllFoldersAsync();
    var allMedia = await _mediaStorage.GetAllMediaAsync();
    var tombstoneNotes = await _noteRepo.GetDeletionTombstonesAsync();
    var tombstoneFolders = await _folderRepo.GetDeletionTombstonesAsync();

    const string FMT = "yyyy-MM-ddTHH:mm:ssZ";
    var manifestRequest = new SyncManifestRequest
    {
      Notes = allNotes.ToDictionary(n => n.Id, n => n.Modified.ToUniversalTime().ToString(FMT)),
      Folders = allFolders.ToDictionary(f => f.Id, f => f.Modified.ToUniversalTime().ToString(FMT)),
      Media = allMedia.ToDictionary(m => m.Id, m => m.Created.ToUniversalTime().ToString(FMT)),
      DeletedNotes = tombstoneNotes,
      DeletedFolders = tombstoneFolders,
    };

    ManifestResponse? manifestResp = await _apiClient.PostManifestAsync(manifestRequest);
    if (manifestResp == null)
      throw new InvalidOperationException("Server did not respond to the manifest request. Check the URL and token.");

    foreach (var id in tombstoneNotes.Keys) await _noteRepo.ClearTombstoneAsync(id);
    foreach (var id in tombstoneFolders.Keys) await _folderRepo.ClearTombstoneAsync(id);

    _toUploadNotes = manifestResp.ToUpload.Notes;
    _toUploadFolders = manifestResp.ToUpload.Folders;
    _toUploadMedia = manifestResp.ToUpload.Media;
    DebugLogService.Current?.Log($"manifest: ul(n={_toUploadNotes.Count} f={_toUploadFolders.Count} m={_toUploadMedia.Count}) dl(n={manifestResp.ToDownload.Notes.Count} f={manifestResp.ToDownload.Folders.Count} m={manifestResp.ToDownload.Media.Count})");

    PullResponse? pull = await _apiClient.PullChangesAsync(
        manifestResp.ToDownload.Notes,
        manifestResp.ToDownload.Folders,
        manifestResp.ToDownload.Media);
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

    foreach (var item in pull.Media)
    {
      try
      {
        byte[] dec = SyncCryptoHelper.AesDecrypt(Convert.FromBase64String(item.EncryptedData), _syncKey);
        changes.Add(new SyncChange
        {
          Id = item.Id,
          EntityType = SyncEntityType.Media,
          ChangeType = SyncChangeType.Update,
          Data = Encoding.UTF8.GetString(dec),
          Timestamp = DateTime.Parse(item.Modified, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime(),
        });
      }
      catch { /* skip undecryptable item */ }
    }

    foreach (var id in manifestResp.ToDeleteLocal.Notes)
      changes.Add(new SyncChange { Id = id, EntityType = SyncEntityType.Note, ChangeType = SyncChangeType.Delete, Timestamp = DateTime.UtcNow });

    foreach (var id in manifestResp.ToDeleteLocal.Folders)
      changes.Add(new SyncChange { Id = id, EntityType = SyncEntityType.Folder, ChangeType = SyncChangeType.Delete, Timestamp = DateTime.UtcNow });

    return changes;
  }

  public async Task ApplyChangesAsync(List<SyncChange> localChanges)
  {
    if (_apiClient == null || _syncKey == null) return;

    const string FMT = "yyyy-MM-ddTHH:mm:ssZ";
    var syncNotes = new List<SyncItem>();
    var syncFolders = new List<SyncItem>();
    var syncMedia = new List<SyncItem>();

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

    foreach (var c in localChanges.Where(c => c.EntityType == SyncEntityType.Media
        && c.ChangeType != SyncChangeType.Delete
        && _toUploadMedia.Contains(c.Id)))
    {
      byte[] enc = SyncCryptoHelper.AesEncrypt(Encoding.UTF8.GetBytes(c.Data), _syncKey);
      syncMedia.Add(new SyncItem
      {
        Id = c.Id,
        EncryptedData = Convert.ToBase64String(enc),
        Modified = c.Timestamp.ToUniversalTime().ToString(FMT),
      });
    }

    // Notes and folders are small — send in one request via the push endpoint.
    if (syncNotes.Count > 0 || syncFolders.Count > 0)
      await _apiClient.PushChangesAsync(syncNotes, syncFolders, new(), new(), new(), _settings?.DeviceId);

    // Each media item is uploaded in 4 MB chunks so large files survive slow/unstable connections.
    foreach (var item in syncMedia)
    {
      DebugLogService.Current?.Log($"full-sync-media: id={item.Id} encChars={item.EncryptedData.Length}");
      await _apiClient.PushChunkedAsync(item, "media", _settings?.DeviceId);
      DebugLogService.Current?.Log($"full-sync-media-done: id={item.Id}");
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
