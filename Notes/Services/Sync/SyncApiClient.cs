using System.Net.Http.Headers;
using System.Security.Cryptography;
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

public class ChunkPushRequest
{
  public string Id { get; set; } = string.Empty;
  public string Type { get; set; } = string.Empty;
  public int ChunkIndex { get; set; }
  public int TotalChunks { get; set; }
  public string Data { get; set; } = string.Empty;
  public string ChunkSha256 { get; set; } = string.Empty;
  public string ItemSha256 { get; set; } = string.Empty;
  public string Modified { get; set; } = string.Empty;
  public string? DeviceId { get; set; }
}

public class SyncApiClient : IDisposable
{
  // 4 MB per chunk — standard for chunked upload protocols (S3 multipart, Azure Blob).
  // Fits comfortably within per-chunk timeout even on slow connections.
  private const int ChunkSize = 4 * 1024 * 1024;

  private readonly HttpClient _http;
  private readonly HttpClient _pushHttp;
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
    // Each 4 MB chunk should transfer in under 5 minutes even on a slow connection.
    _pushHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    _pushHttp.DefaultRequestHeaders.Authorization = auth;
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
      catch (HttpRequestException)
      {
        if (attempt == 0)
          await Task.Delay(700);
        else
          return default;
      }
    }
    return default;
  }

  public async Task RegisterDeviceAsync(string deviceId)
  {
    try
    {
      using var resp = await _http.PostAsync(_baseUrl + "/api/sync/register",
          Json(new { device_id = deviceId }));
      _ = resp.IsSuccessStatusCode;
    }
    catch (HttpRequestException) { }
  }

  public async Task<ManifestResponse?> PostManifestAsync(SyncManifestRequest manifest)
  {
    return await PostAsync<ManifestResponse>("/api/sync/manifest", manifest);
  }

  public async Task<bool> PushChangesAsync(List<SyncItem> notes, List<SyncItem> folders,
      List<SyncItem> media, List<string> deletedNotes, List<string> deletedFolders, string? deviceId = null)
  {
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
      catch (HttpRequestException)
      {
        if (attempt == 0)
          await Task.Delay(700);
        else
          return false;
      }
    }
    return false;
  }

  // Sends one chunk to /api/sync/chunk. Returns false on hash mismatch (4xx) — no retry.
  // Retries up to 3 times on network/timeout errors with exponential backoff.
  private async Task<bool> PushChunkAsync(ChunkPushRequest req)
  {
    string json = JsonSerializer.Serialize(req, JsonOpts);
    for (int attempt = 0; attempt < 3; attempt++)
    {
      try
      {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _pushHttp.PostAsync(_baseUrl + "/api/sync/chunk", content);
        if (resp.IsSuccessStatusCode) return true;
        if ((int)resp.StatusCode is >= 400 and < 500) return false;
      }
      catch
      {
        if (attempt == 2) return false;
        await Task.Delay(TimeSpan.FromSeconds(attempt + 1));
      }
    }
    return false;
  }

  // Splits item.EncryptedData into 4 MB chunks and uploads each independently.
  // SHA256 is verified per chunk and for the fully assembled item on the server.
  // If a chunk fails after retries, the upload stops; the server will re-request
  // missing chunks on the next manifest exchange.
  public async Task PushChunkedAsync(SyncItem item, string type, string? deviceId = null)
  {
    byte[] base64Bytes = Encoding.UTF8.GetBytes(item.EncryptedData);
    int totalChunks = Math.Max(1, (base64Bytes.Length + ChunkSize - 1) / ChunkSize);
    string itemSha256 = Convert.ToHexString(SHA256.HashData(base64Bytes)).ToLower();

    for (int i = 0; i < totalChunks; i++)
    {
      int offset = i * ChunkSize;
      int length = Math.Min(ChunkSize, base64Bytes.Length - offset);
      // Slice the base64 string at 4-byte boundaries (base64 encodes 3 bytes → 4 chars),
      // so any aligned split produces valid substrings that concatenate back cleanly.
      string chunkData = Encoding.UTF8.GetString(base64Bytes, offset, length);
      string chunkSha256 = Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(chunkData))).ToLower();

      var req = new ChunkPushRequest
      {
        Id = item.Id,
        Type = type,
        ChunkIndex = i,
        TotalChunks = totalChunks,
        Data = chunkData,
        ChunkSha256 = chunkSha256,
        ItemSha256 = itemSha256,
        Modified = item.Modified,
        DeviceId = deviceId,
      };

      if (!await PushChunkAsync(req)) return;
    }
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
    _pushHttp.Dispose();
    _sseHttp.Dispose();
  }
}
