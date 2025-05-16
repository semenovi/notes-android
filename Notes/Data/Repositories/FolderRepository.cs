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
    List<Folder> result = new List<Folder>();
    List<string> files = _storage.GetFiles(FOLDERS_FOLDER);

    foreach (string file in files)
    {
      if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
      {
        Folder? folder = await _storage.ReadJsonAsync<Folder>(file);
        if (folder != null)
          result.Add(folder);
      }
    }

    return result;
  }

  public async Task SaveFolderAsync(Folder folder)
  {
    string path = GetFolderPath(folder.Id);
    await _storage.WriteJsonAsync(path, folder);
  }

  public async Task<bool> DeleteFolderAsync(string id)
  {
    string path = GetFolderPath(id);
    return await _storage.DeleteFileAsync(path);
  }

  private string GetFolderPath(string id)
  {
    return Path.Combine(FOLDERS_FOLDER, $"{id}.json");
  }
}