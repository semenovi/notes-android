using Notes.Data.Repositories;
using Notes.Models;

namespace Notes.Services.Notes;

public class FolderManager
{
  private readonly FolderRepository _repository;

  public event Action<string, EntityChangeKind>? FolderChanged;

  public FolderManager(FolderRepository repository)
  {
    _repository = repository;
  }

  public async Task<Folder> CreateFolderAsync(string name, string? parentId = null)
  {
    var folder = new Folder
    {
      Name = name,
      ParentId = parentId
    };

    await _repository.SaveFolderAsync(folder);
    FolderChanged?.Invoke(folder.Id, EntityChangeKind.Created);
    return folder;
  }

  public async Task UpdateFolderAsync(Folder folder)
  {
    await _repository.SaveFolderAsync(folder);
    FolderChanged?.Invoke(folder.Id, EntityChangeKind.Updated);
  }

  public async Task<bool> DeleteFolderAsync(string folderId)
  {
    var result = await _repository.DeleteFolderAsync(folderId);
    if (result) FolderChanged?.Invoke(folderId, EntityChangeKind.Deleted);
    return result;
  }

  public async Task<Folder?> GetFolderAsync(string id)
  {
    return await _repository.GetFolderAsync(id);
  }

  public async Task<List<Folder>> GetFoldersAsync(string? parentId = null)
  {
    return await _repository.GetFoldersAsync(parentId);
  }

  public async Task<List<Folder>> GetAllFoldersAsync()
  {
    return await _repository.GetAllFoldersAsync();
  }

  public async Task<bool> MoveFolderAsync(string folderId, string? newParentId)
  {
    var folder = await _repository.GetFolderAsync(folderId);

    if (folder == null)
      return false;

    folder.ParentId = newParentId;
    await _repository.SaveFolderAsync(folder);
    FolderChanged?.Invoke(folderId, EntityChangeKind.Updated);
    return true;
  }

  // All folders nested anywhere under folderId, at any depth. Guards against a
  // parent cycle (two folders reparented into each other by conflicting syncs
  // from different devices) sending Collect into infinite recursion.
  public async Task<List<Folder>> GetDescendantFoldersAsync(string folderId)
  {
    var all = await _repository.GetAllFoldersAsync();
    var result = new List<Folder>();
    var visited = new HashSet<string> { folderId };

    void Collect(string parentId)
    {
      foreach (var f in all.Where(f => f.ParentId == parentId))
      {
        if (!visited.Add(f.Id)) continue;
        result.Add(f);
        Collect(f.Id);
      }
    }

    Collect(folderId);
    return result;
  }
}