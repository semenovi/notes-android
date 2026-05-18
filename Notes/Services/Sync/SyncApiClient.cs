using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notes.Services.Sync;

public class SseEvent
{
  public string Type { get; set; } = string.Empty;
  public List<string> Notes { get; set; } = new();
  public List<string> Folders { get; set; } = new();
  public List<string> DeletedNotes { get; set; } = new();
  public List<string> DeletedFolders { get; set; } = new();
}

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
  private readonly HttpClient _sseHttp;
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
    var auth = new AuthenticationHeaderValue("Bearer", apiToken);
    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    _http.DefaultRequestHeaders.Authorization = auth;
    _sseHttp = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    _sseHttp.DefaultRequestHeaders.Authorization = auth;
  }

  private StringContent Json(object body) =>
      new(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

  private async Task<T?> PostAsync<T>(string path, object body)
  {
    for (int attempt = 0; attempt < 2; attempt++)
    {
      try
      {
        using var resp = await _http.PostAsync(_baseUrl + path, Json(body));
        if (!resp.IsSuccessStatusCode) return default;
        return JsonSerializer.Deserialize<T>(await resp.Content.ReadAsStringAsync(), JsonOpts);
      }
      catch (HttpRequestException) when (attempt == 0)
      {
        await Task.Delay(700);
      }
    }
    return default;
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
      List<SyncItem> media, List<string> deletedNotes, List<string> deletedFolders, string? deviceId = null)
  {
    // Serialize once; recreate StringContent per attempt (HttpContent cannot be re-sent).
    string json = JsonSerializer.Serialize(
        new { notes, folders, media, deleted_notes = deletedNotes, deleted_folders = deletedFolders, device_id = deviceId },
        JsonOpts);
    for (int attempt = 0; attempt < 2; attempt++)
    {
      try
      {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(_baseUrl + "/api/sync/push", body);
        if (resp.IsSuccessStatusCode) return true;
      }
      catch (HttpRequestException) when (attempt == 0)
      {
        await Task.Delay(700);
      }
    }
    return false;
  }

  public async Task SubscribeToEventsAsync(string deviceId, Action<SseEvent> onEvent, CancellationToken ct)
  {
    var url = _baseUrl + "/api/sync/events?device_id=" + Uri.EscapeDataString(deviceId);
    using var resp = await _sseHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
    if (!resp.IsSuccessStatusCode) return;
    using var stream = await resp.Content.ReadAsStreamAsync(ct);
    using var reader = new StreamReader(stream);
    while (!ct.IsCancellationRequested)
    {
      string? line = await reader.ReadLineAsync(ct);
      if (line == null) break;
      if (!line.StartsWith("data: ")) continue;
      try
      {
        var evt = JsonSerializer.Deserialize<SseEvent>(line["data: ".Length..], JsonOpts);
        if (evt != null && evt.Type != "connected") onEvent(evt);
      }
      catch { }
    }
  }

  public async Task<PullResponse?> PullChangesAsync(List<string> noteIds, List<string> folderIds, List<string> mediaIds)
  {
    if (noteIds.Count == 0 && folderIds.Count == 0 && mediaIds.Count == 0) return new PullResponse();
    return await PostAsync<PullResponse>("/api/sync/pull",
        new { note_ids = noteIds, folder_ids = folderIds, media_ids = mediaIds });
  }

  public void Dispose()
  {
    _http.Dispose();
    _sseHttp.Dispose();
  }
}
