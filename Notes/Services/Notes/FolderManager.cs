using Notes.Data.Repositories;
using Notes.Models;

namespace Notes.Services.Notes;

public class FolderManager
{
  private readonly FolderRepository _repository;

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
    return folder;
  }

  public async Task UpdateFolderAsync(Folder folder)
  {
    await _repository.SaveFolderAsync(folder);
  }

  public async Task<bool> DeleteFolderAsync(string folderId)
  {
    return await _repository.DeleteFolderAsync(folderId);
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
    return true;
  }
}