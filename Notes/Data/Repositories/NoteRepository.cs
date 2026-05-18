using System.Text;
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

  public async Task<bool> DeleteNoteAsync(string id, bool createTombstone = true)
  {
    string path = GetNotePath(id);
    bool deleted = await _storage.DeleteFileAsync(path);
    if (deleted && createTombstone)
    {
      string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
      await _storage.WriteFileAsync(
          Path.Combine(NOTES_FOLDER, $"{id}.deleted"),
          Encoding.UTF8.GetBytes(ts));
    }
    return deleted;
  }

  public async Task<Dictionary<string, string>> GetDeletionTombstonesAsync()
  {
    var result = new Dictionary<string, string>();
    foreach (var file in _storage.GetFiles(NOTES_FOLDER).Where(f => f.EndsWith(".deleted")))
    {
      string noteId = Path.GetFileNameWithoutExtension(file);
      byte[] data = await _storage.ReadFileAsync(file);
      result[noteId] = Encoding.UTF8.GetString(data);
    }
    return result;
  }

  public async Task ClearTombstoneAsync(string id)
  {
    await _storage.DeleteFileAsync(Path.Combine(NOTES_FOLDER, $"{id}.deleted"));
  }

  public async Task SaveNoteSyncAsync(Note note)
  {
    await _storage.WriteJsonAsync(GetNotePath(note.Id), note);
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