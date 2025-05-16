using Notes.Models;

namespace Notes.Data.Repositories;

public class NoteRepository
{
  private readonly Storage.FileSystemStorage _storage;
  private const string NOTES_FOLDER = "Notes";

  public NoteRepository(Storage.FileSystemStorage storage)
  {
    _storage = storage;
  }

  public async Task<Note?> GetNoteAsync(string id)
  {
    string path = GetNotePath(id);
    return await _storage.ReadJsonAsync<Note>(path);
  }

  public async Task<List<Note>> GetNotesAsync(string folderId)
  {
    List<Note> allNotes = await GetAllNotesAsync();
    return allNotes.Where(n => n.FolderId == folderId).ToList();
  }

  public async Task<List<Note>> GetAllNotesAsync()
  {
    List<Note> result = new List<Note>();
    List<string> files = _storage.GetFiles(NOTES_FOLDER);

    foreach (string file in files)
    {
      if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
      {
        Note? note = await _storage.ReadJsonAsync<Note>(file);
        if (note != null)
          result.Add(note);
      }
    }

    return result;
  }

  public async Task SaveNoteAsync(Note note)
  {
    note.Modified = DateTime.Now;
    string path = GetNotePath(note.Id);
    await _storage.WriteJsonAsync(path, note);
  }

  public async Task<bool> DeleteNoteAsync(string id)
  {
    string path = GetNotePath(id);
    return await _storage.DeleteFileAsync(path);
  }

  public async Task<List<Note>> SearchNotesAsync(string query)
  {
    List<Note> allNotes = await GetAllNotesAsync();

    if (string.IsNullOrWhiteSpace(query))
      return allNotes;

    query = query.ToLowerInvariant();

    return allNotes.Where(n =>
        n.Title.ToLowerInvariant().Contains(query) ||
        n.Content.ToLowerInvariant().Contains(query) ||
        n.Tags.Any(t => t.ToLowerInvariant().Contains(query))
    ).ToList();
  }

  private string GetNotePath(string id)
  {
    return Path.Combine(NOTES_FOLDER, $"{id}.json");
  }
}