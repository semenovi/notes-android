using Notes.Data.Repositories;
using Notes.Models;

namespace Notes.Services.Notes;

public enum EntityChangeKind { Created, Updated, Deleted }

public class NoteManager
{
  private readonly NoteRepository _repository;

  public event Action<string, EntityChangeKind>? NoteChanged;

  public NoteManager(NoteRepository repository)
  {
    _repository = repository;
  }

  public async Task<Note> CreateNoteAsync(string title, string folderId)
  {
    var note = new Note
    {
      Title = title,
      FolderId = folderId,
      Created = DateTime.Now,
      Modified = DateTime.Now
    };

    await _repository.SaveNoteAsync(note);
    NoteChanged?.Invoke(note.Id, EntityChangeKind.Created);
    return note;
  }

  public async Task UpdateNoteAsync(Note note)
  {
    note.Modified = DateTime.Now;
    await _repository.SaveNoteAsync(note);
    NoteChanged?.Invoke(note.Id, EntityChangeKind.Updated);
  }

  public async Task<bool> DeleteNoteAsync(string noteId)
  {
    var result = await _repository.DeleteNoteAsync(noteId);
    if (result) NoteChanged?.Invoke(noteId, EntityChangeKind.Deleted);
    return result;
  }

  public async Task<Note?> GetNoteAsync(string id)
  {
    return await _repository.GetNoteAsync(id);
  }

  public async Task<List<Note>> GetNotesAsync(string folderId)
  {
    return await _repository.GetNotesAsync(folderId);
  }

  public async Task<List<Note>> SearchNotesAsync(string query)
  {
    return await _repository.SearchNotesAsync(query);
  }
}