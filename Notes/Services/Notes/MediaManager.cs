using Notes.Data.Storage;
using Notes.Models;

namespace Notes.Services.Notes;

public class MediaManager
{
  private readonly MediaStorage _storage;

  public event Action<string>? MediaAdded;
  public event Action<string>? MediaDeleted;

  public MediaManager(MediaStorage storage)
  {
    _storage = storage;
  }

  public async Task<MediaItem> AddMediaAsync(Stream mediaStream, string fileName)
  {
    var item = await _storage.AddMediaAsync(mediaStream, fileName);
    MediaAdded?.Invoke(item.Id);
    return item;
  }

  public async Task<MediaItem?> GetMediaAsync(string mediaId)
  {
    return await _storage.GetMediaAsync(mediaId);
  }

  public async Task<Stream> GetMediaContentAsync(string mediaId)
  {
    return await _storage.GetMediaContentAsync(mediaId);
  }

  public async Task<byte[]> GetRawContentAsync(string mediaId)
  {
    return await _storage.GetRawContentAsync(mediaId);
  }

  public async Task<bool> DeleteMediaAsync(string mediaId, bool createTombstone = true)
  {
    var result = await _storage.DeleteMediaAsync(mediaId, createTombstone);
    if (result)
      MediaDeleted?.Invoke(mediaId);
    return result;
  }

  public async Task<List<MediaItem>> GetAllMediaAsync()
  {
    return await _storage.GetAllMediaAsync();
  }

  public string GetMediaUrl(string mediaId)
  {
    return $"media:{mediaId}";
  }
}