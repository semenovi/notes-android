using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Notes.Models;
using Notes.Services;

namespace Notes.Data.Storage;

public class MediaStorage
{
  private readonly FileSystemStorage _storage;
  private const string MEDIA_FOLDER = "Media";

  public MediaStorage(FileSystemStorage storage)
  {
    _storage = storage;
  }

  public string StoragePath => MEDIA_FOLDER;

  public async Task<MediaItem> AddMediaAsync(Stream mediaStream, string fileName)
  {
    string fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
    string fileType = fileExtension.TrimStart('.');
    string mediaId = Guid.NewGuid().ToString();
    string storagePath = Path.Combine(MEDIA_FOLDER, $"{mediaId}{fileExtension}");

    using var ms = new MemoryStream();
    await mediaStream.CopyToAsync(ms);
    byte[] data = ms.ToArray();

    bool isRaster = fileType is "jpg" or "jpeg" or "png" or "webp";
    if (isRaster)
    {
      int before = data.Length;
      data = await Task.Run(() => ResizeImage(data));
      if (data.Length != before)
        DebugLogService.Current?.Log($"media-resize: {mediaId} {before}→{data.Length} bytes");
    }

    string contentHash = Convert.ToHexString(SHA256.HashData(data)).ToLower();
    var existing = await GetAllMediaAsync();
    var duplicate = existing.FirstOrDefault(m => !string.IsNullOrEmpty(m.ContentHash) && m.ContentHash == contentHash);
    if (duplicate != null)
    {
      DebugLogService.Current?.Log($"media-dedup: reusing {duplicate.Id} for {fileName}");
      return duplicate;
    }

    await _storage.WriteFileAsync(storagePath, data);

    var mediaItem = new MediaItem
    {
      Id = mediaId,
      FileName = fileName,
      FileType = fileType,
      StoragePath = storagePath,
      Size = data.Length,
      Created = DateTime.Now,
      ContentHash = contentHash,
    };

    await SaveMediaMetadataAsync(mediaItem);
    return mediaItem;
  }

  private static byte[] ResizeImage(byte[] data)
  {
    const int MaxDim = 2048;
    try
    {
      using var inMs = new MemoryStream(data);
      var img = PlatformImage.FromStream(inMs);
      if (img.Width <= MaxDim && img.Height <= MaxDim)
        return data;
      float scale = Math.Min((float)MaxDim / img.Width, (float)MaxDim / img.Height);
      var resized = img.Resize((int)(img.Width * scale), (int)(img.Height * scale), ResizeMode.Fit);
      using var outMs = new MemoryStream();
      resized.Save(outMs);
      return outMs.ToArray();
    }
    catch
    {
      return data;
    }
  }

  public async Task<bool> DeleteMediaAsync(string mediaId, bool createTombstone = true)
  {
    MediaItem? mediaItem = await GetMediaAsync(mediaId);

    if (mediaItem == null)
      return false;

    bool fileDeleted = await _storage.DeleteFileAsync(mediaItem.StoragePath);
    bool metadataDeleted = await _storage.DeleteFileAsync(GetMediaMetadataPath(mediaId));

    if (fileDeleted && metadataDeleted && createTombstone)
    {
      string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
      await _storage.WriteFileAsync(
          Path.Combine(MEDIA_FOLDER, $"{mediaId}.deleted"),
          Encoding.UTF8.GetBytes(ts));
    }

    return fileDeleted && metadataDeleted;
  }

  public async Task<Dictionary<string, string>> GetDeletionTombstonesAsync()
  {
    var files = _storage.GetFiles(MEDIA_FOLDER).Where(f => f.EndsWith(".deleted")).ToList();
    var tasks = files.Select(async f =>
    {
      string id = Path.GetFileNameWithoutExtension(f);
      byte[] data = await _storage.ReadFileAsync(f);
      return (id, ts: Encoding.UTF8.GetString(data));
    });
    var results = await Task.WhenAll(tasks);
    return results.ToDictionary(r => r.id, r => r.ts);
  }

  public async Task ClearTombstoneAsync(string mediaId)
  {
    await _storage.DeleteFileAsync(Path.Combine(MEDIA_FOLDER, $"{mediaId}.deleted"));
  }

  public async Task<MediaItem?> GetMediaAsync(string mediaId)
  {
    return await _storage.ReadJsonAsync<MediaItem>(GetMediaMetadataPath(mediaId));
  }

  public async Task<List<MediaItem>> GetAllMediaAsync()
  {
    string metadataDir = Path.Combine(MEDIA_FOLDER, "Metadata");
    var files = _storage.GetFiles(metadataDir);
    var results = await Task.WhenAll(files.Select(f => _storage.ReadJsonAsync<MediaItem>(f)));
    return results.Where(m => m != null).ToList()!;
  }

  public async Task<Stream> GetMediaContentAsync(string mediaId)
  {
    MediaItem? mediaItem = await GetMediaAsync(mediaId);

    if (mediaItem == null)
      throw new FileNotFoundException($"Media with id {mediaId} not found");

    byte[] data = await _storage.ReadFileAsync(mediaItem.StoragePath);
    return new MemoryStream(data);
  }

  public async Task<byte[]> GetRawContentAsync(string mediaId)
  {
    MediaItem? item = await GetMediaAsync(mediaId);
    if (item == null) throw new FileNotFoundException($"Media {mediaId} not found");
    return await _storage.ReadFileAsync(item.StoragePath);
  }

  public async Task SaveMediaFromSyncAsync(MediaItem metadata, byte[] content)
  {
    string fileType = metadata.FileType?.ToLowerInvariant() ?? "";
    bool isRaster = fileType is "jpg" or "jpeg" or "png" or "webp";
    if (isRaster)
      content = await Task.Run(() => ResizeImage(content));
    metadata.Size = content.Length;
    await _storage.WriteFileAsync(metadata.StoragePath, content);
    await SaveMediaMetadataAsync(metadata);
  }

  public async Task MigrateExistingMediaAsync()
  {
    var items = await GetAllMediaAsync();
    foreach (var item in items)
    {
      string fileType = item.FileType?.ToLowerInvariant() ?? "";
      bool isRaster = fileType is "jpg" or "jpeg" or "png" or "webp";
      if (!isRaster) continue;

      try
      {
        byte[] data = await _storage.ReadFileAsync(item.StoragePath);
        byte[] resized = ResizeImage(data);
        if (resized.Length < data.Length)
        {
          await _storage.WriteFileAsync(item.StoragePath, resized);
          item.Size = resized.Length;
          await SaveMediaMetadataAsync(item);
        }
      }
      catch { }
    }
  }

  private async Task SaveMediaMetadataAsync(MediaItem mediaItem)
  {
    string path = GetMediaMetadataPath(mediaItem.Id);
    await _storage.WriteJsonAsync(path, mediaItem);
  }

  private string GetMediaMetadataPath(string mediaId)
  {
    return Path.Combine(MEDIA_FOLDER, "Metadata", $"{mediaId}.json");
  }
}