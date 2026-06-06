using System.Text;
using Notes.Models;

namespace Notes.Data.Repositories;

public class FolderRepository
{
  private readonly Storage.FileSystemStorage _storage;
  private const string FOLDERS_FOLDER = "Folders";

  public FolderRepository(Storage.FileSystemStorage storage)
  {
    _storage = storage;
  }

  public async Task<Folder?> GetFolderAsync(string id)
  {
    string path = GetFolderPath(id);
    return await _storage.ReadJsonAsync<Folder>(path);
  }

  public async Task<List<Folder>> GetFoldersAsync(string? parentId = null)
  {
    List<Folder> allFolders = await GetAllFoldersAsync();

    if (parentId == null)
      return allFolders.Where(f => f.ParentId == null).ToList();

    return allFolders.Where(f => f.ParentId == parentId).ToList();
  }

  public async Task<List<Folder>> GetAllFoldersAsync()
  {
    var files = _storage.GetFiles(FOLDERS_FOLDER)
        .Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
        .ToList();
    var results = await Task.WhenAll(files.Select(f => _storage.ReadJsonAsync<Folder>(f)));
    return results.Where(f => f != null).ToList()!;
  }

  public async Task SaveFolderAsync(Folder folder)
  {
    folder.Modified = DateTime.Now;
    string path = GetFolderPath(folder.Id);
    await _storage.WriteJsonAsync(path, folder);
  }

  public async Task SaveFolderSyncAsync(Folder folder)
  {
    await _storage.WriteJsonAsync(GetFolderPath(folder.Id), folder);
  }

  public async Task<bool> DeleteFolderAsync(string id, bool createTombstone = true)
  {
    string path = GetFolderPath(id);
    bool deleted = await _storage.DeleteFileAsync(path);
    if (deleted && createTombstone)
    {
      string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
      await _storage.WriteFileAsync(
          Path.Combine(FOLDERS_FOLDER, $"{id}.deleted"),
          Encoding.UTF8.GetBytes(ts));
    }
    return deleted;
  }

  public async Task<Dictionary<string, string>> GetDeletionTombstonesAsync()
  {
    var files = _storage.GetFiles(FOLDERS_FOLDER).Where(f => f.EndsWith(".deleted")).ToList();
    var tasks = files.Select(async f =>
    {
      string id = Path.GetFileNameWithoutExtension(f);
      byte[] data = await _storage.ReadFileAsync(f);
      return (id, ts: Encoding.UTF8.GetString(data));
    });
    var results = await Task.WhenAll(tasks);
    return results.ToDictionary(r => r.id, r => r.ts);
  }

  public async Task ClearTombstoneAsync(string id)
  {
    await _storage.DeleteFileAsync(Path.Combine(FOLDERS_FOLDER, $"{id}.deleted"));
  }

  private string GetFolderPath(string id)
  {
    return Path.Combine(FOLDERS_FOLDER, $"{id}.json");
  }
}