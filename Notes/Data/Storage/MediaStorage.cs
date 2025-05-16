using Notes.Models;

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
    string fileExtension = Path.GetExtension(fileName);
    string fileType = fileExtension.TrimStart('.');
    string mediaId = Guid.NewGuid().ToString();
    string storagePath = Path.Combine(MEDIA_FOLDER, $"{mediaId}{fileExtension}");

    using (MemoryStream ms = new MemoryStream())
    {
      await mediaStream.CopyToAsync(ms);
      byte[] data = ms.ToArray();
      await _storage.WriteFileAsync(storagePath, data);

      MediaItem mediaItem = new MediaItem
      {
        Id = mediaId,
        FileName = fileName,
        FileType = fileType,
        StoragePath = storagePath,
        Size = data.Length,
        Created = DateTime.Now
      };

      await SaveMediaMetadataAsync(mediaItem);
      return mediaItem;
    }
  }

  public async Task<bool> DeleteMediaAsync(string mediaId)
  {
    MediaItem? mediaItem = await GetMediaAsync(mediaId);

    if (mediaItem == null)
      return false;

    bool fileDeleted = await _storage.DeleteFileAsync(mediaItem.StoragePath);
    bool metadataDeleted = await _storage.DeleteFileAsync(GetMediaMetadataPath(mediaId));

    return fileDeleted && metadataDeleted;
  }

  public async Task<MediaItem?> GetMediaAsync(string mediaId)
  {
    return await _storage.ReadJsonAsync<MediaItem>(GetMediaMetadataPath(mediaId));
  }

  public async Task<List<MediaItem>> GetAllMediaAsync()
  {
    List<MediaItem> result = new List<MediaItem>();
    string metadataDir = Path.Combine(MEDIA_FOLDER, "Metadata");
    List<string> files = _storage.GetFiles(metadataDir);

    foreach (string file in files)
    {
      MediaItem? mediaItem = await _storage.ReadJsonAsync<MediaItem>(file);
      if (mediaItem != null)
        result.Add(mediaItem);
    }

    return result;
  }

  public async Task<Stream> GetMediaContentAsync(string mediaId)
  {
    MediaItem? mediaItem = await GetMediaAsync(mediaId);

    if (mediaItem == null)
      throw new FileNotFoundException($"Media with id {mediaId} not found");

    byte[] data = await _storage.ReadFileAsync(mediaItem.StoragePath);
    return new MemoryStream(data);
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