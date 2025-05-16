using Notes.Data.Repositories;
using Notes.Models;

namespace Notes.Services.Notes;

public class NoteManager
{
  private readonly NoteRepository _repository;

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
    return note;
  }

  public async Task UpdateNoteAsync(Note note)
  {
    note.Modified = DateTime.Now;
    await _repository.SaveNoteAsync(note);
  }

  public async Task<bool> DeleteNoteAsync(string noteId)
  {
    return await _repository.DeleteNoteAsync(noteId);
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