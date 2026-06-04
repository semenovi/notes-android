using System.Text.RegularExpressions;
using Notes.Data.Repositories;
using Notes.Models;

namespace Notes.Services.Notes;

public enum EntityChangeKind { Created, Updated, Deleted }

public class NoteManager
{
  private readonly NoteRepository _repository;
  private readonly MediaManager _mediaManager;

  private static readonly Regex MediaRefRegex =
      new(@"!\[.*?\]\(media:([\w-]+)\)", RegexOptions.Compiled);

  public event Action<string, EntityChangeKind>? NoteChanged;

  public NoteManager(NoteRepository repository, MediaManager mediaManager)
  {
    _repository = repository;
    _mediaManager = mediaManager;
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
    var oldNote = await _repository.GetNoteAsync(note.Id);
    note.Modified = DateTime.Now;
    await _repository.SaveNoteAsync(note);
    NoteChanged?.Invoke(note.Id, EntityChangeKind.Updated);

    if (oldNote != null)
    {
      var oldIds = ExtractMediaIds(oldNote.Content);
      var newIds = ExtractMediaIds(note.Content);
      var removed = oldIds.Except(newIds).ToList();
      if (removed.Count > 0)
        await CleanupOrphanMediaAsync(removed);
    }
  }

  public async Task<bool> DeleteNoteAsync(string noteId)
  {
    var note = await _repository.GetNoteAsync(noteId);
    var result = await _repository.DeleteNoteAsync(noteId);
    if (!result) return false;

    NoteChanged?.Invoke(noteId, EntityChangeKind.Deleted);

    if (note != null)
    {
      var mediaIds = ExtractMediaIds(note.Content);
      if (mediaIds.Count > 0)
        await CleanupOrphanMediaAsync(mediaIds);
    }

    return true;
  }

  private static List<string> ExtractMediaIds(string content) =>
      MediaRefRegex.Matches(content).Select(m => m.Groups[1].Value).Distinct().ToList();

  private async Task CleanupOrphanMediaAsync(IEnumerable<string> candidateIds)
  {
    var remainingNotes = await _repository.GetAllNotesAsync();
    var referencedMedia = new HashSet<string>(
        remainingNotes.SelectMany(n => ExtractMediaIds(n.Content)));

    foreach (var mediaId in candidateIds)
    {
      if (!referencedMedia.Contains(mediaId))
        await _mediaManager.DeleteMediaAsync(mediaId);
    }
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