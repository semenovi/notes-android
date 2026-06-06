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
    var files = _storage.GetFiles(NOTES_FOLDER)
        .Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
        .ToList();
    var results = await Task.WhenAll(files.Select(f => _storage.ReadJsonAsync<Note>(f)));
    return results.Where(n => n != null).ToList()!;
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
    var files = _storage.GetFiles(NOTES_FOLDER).Where(f => f.EndsWith(".deleted")).ToList();
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