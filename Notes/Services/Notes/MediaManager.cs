using Notes.Data.Storage;
using Notes.Models;

namespace Notes.Services.Notes;

public class MediaManager
{
  private readonly MediaStorage _storage;

  public MediaManager(MediaStorage storage)
  {
    _storage = storage;
  }

  public async Task<MediaItem> AddMediaAsync(Stream mediaStream, string fileName)
  {
    return await _storage.AddMediaAsync(mediaStream, fileName);
  }

  public async Task<MediaItem?> GetMediaAsync(string mediaId)
  {
    return await _storage.GetMediaAsync(mediaId);
  }

  public async Task<Stream> GetMediaContentAsync(string mediaId)
  {
    return await _storage.GetMediaContentAsync(mediaId);
  }

  public async Task<bool> DeleteMediaAsync(string mediaId)
  {
    return await _storage.DeleteMediaAsync(mediaId);
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