using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notes.Services.Sync;

public class SyncManifestRequest
{
  public Dictionary<string, string> Notes { get; set; } = new();
  public Dictionary<string, string> Folders { get; set; } = new();
  public Dictionary<string, string> Media { get; set; } = new();
  public Dictionary<string, string> DeletedNotes { get; set; } = new();
  public Dictionary<string, string> DeletedFolders { get; set; } = new();
}

public class ManifestResponse
{
  public EntityLists ToDownload { get; set; } = new();
  public EntityLists ToUpload { get; set; } = new();
  public EntityLists ToDeleteLocal { get; set; } = new();
}

public class EntityLists
{
  public List<string> Notes { get; set; } = new();
  public List<string> Folders { get; set; } = new();
  public List<string> Media { get; set; } = new();
}

public class SyncItem
{
  public string Id { get; set; } = string.Empty;
  public string EncryptedData { get; set; } = string.Empty;
  public string Modified { get; set; } = string.Empty;
}

public class PullResponse
{
  public List<SyncItem> Notes { get; set; } = new();
  public List<SyncItem> Folders { get; set; } = new();
  public List<SyncItem> Media { get; set; } = new();
}

public class SyncApiClient : IDisposable
{
  private readonly HttpClient _http;
  private readonly string _baseUrl;

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  public SyncApiClient(string baseUrl, string apiToken)
  {
    _baseUrl = baseUrl.TrimEnd('/');
    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    _http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiToken);
  }

  private StringContent Json(object body) =>
      new(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

  private async Task<T?> PostAsync<T>(string path, object body)
  {
    using var resp = await _http.PostAsync(_baseUrl + path, Json(body));
    if (!resp.IsSuccessStatusCode) return default;
    string json = await resp.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<T>(json, JsonOpts);
  }

  public async Task RegisterDeviceAsync(string deviceId)
  {
    using var resp = await _http.PostAsync(_baseUrl + "/api/sync/register",
        Json(new { device_id = deviceId }));
    _ = resp.IsSuccessStatusCode;
  }

  public async Task<ManifestResponse?> PostManifestAsync(SyncManifestRequest manifest)
  {
    return await PostAsync<ManifestResponse>("/api/sync/manifest", manifest);
  }

  public async Task<bool> PushChangesAsync(List<SyncItem> notes, List<SyncItem> folders,
      List<SyncItem> media, List<string> deletedNotes, List<string> deletedFolders)
  {
    using var resp = await _http.PostAsync(_baseUrl + "/api/sync/push",
        Json(new { notes, folders, media, deleted_notes = deletedNotes, deleted_folders = deletedFolders }));
    return resp.IsSuccessStatusCode;
  }

  public async Task<PullResponse?> PullChangesAsync(List<string> noteIds, List<string> folderIds, List<string> mediaIds)
  {
    if (noteIds.Count == 0 && folderIds.Count == 0 && mediaIds.Count == 0) return new PullResponse();
    return await PostAsync<PullResponse>("/api/sync/pull",
        new { note_ids = noteIds, folder_ids = folderIds, media_ids = mediaIds });
  }

  public void Dispose() => _http.Dispose();
}
