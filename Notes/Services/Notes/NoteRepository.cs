using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Notes.Models;
using Notes.Services.Storage;

namespace Notes.Services.Notes
{
    public class NoteRepository
    {
        private readonly FileSystemStorage _storage;
        private readonly string _notesFolder = "notes";
        private readonly Dictionary<string, Note> _cache;

        public NoteRepository(FileSystemStorage storage)
        {
            _storage = storage;
            _storage.CreateDirectory(_notesFolder);
            _cache = new Dictionary<string, Note>();
        }

        public async Task<Note> GetNoteAsync(string id)
        {
            // Check cache first
            if (_cache.TryGetValue(id, out Note cachedNote))
                return cachedNote;

            string notePath = Path.Combine(_notesFolder, $"{id}.json");
            byte[] noteData = await _storage.ReadFileAsync(notePath);
            
            if (noteData == null)
                return null;

            try
            {
                var note = JsonSerializer.Deserialize<Note>(noteData);
                _cache[id] = note;
                return note;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<Note>> GetNotesAsync(string folderId)
        {
            var result = new List<Note>();
            var allNotes = await GetAllNotesAsync();
            
            if (string.IsNullOrEmpty(folderId))
                return allNotes;
                
            return allNotes.Where(n => n.FolderId == folderId).ToList();
        }

        public async Task<List<Note>> GetAllNotesAsync()
        {
            var result = new List<Note>();
            var noteFiles = _storage.GetFiles(_notesFolder);
            
            foreach (var file in noteFiles)
            {
                if (!Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileId = Path.GetFileNameWithoutExtension(file);
                var note = await GetNoteAsync(fileId);
                
                if (note != null)
                    result.Add(note);
            }
            
            return result;
        }

        public async Task SaveNoteAsync(Note note)
        {
            if (note == null)
                throw new ArgumentNullException(nameof(note));

            note.Modified = DateTime.Now;
            string notePath = Path.Combine(_notesFolder, $"{note.Id}.json");
            string json = JsonSerializer.Serialize(note);
            byte[] noteData = System.Text.Encoding.UTF8.GetBytes(json);
            
            await _storage.WriteFileAsync(notePath, noteData);
            _cache[note.Id] = note;
        }

        public async Task DeleteNoteAsync(string id)
        {
            string notePath = Path.Combine(_notesFolder, $"{id}.json");
            _storage.DeleteFile(notePath);
            
            if (_cache.ContainsKey(id))
                _cache.Remove(id);
        }

        public async Task<List<Note>> SearchNotesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return await GetAllNotesAsync();

            var allNotes = await GetAllNotesAsync();
            query = query.ToLowerInvariant();
            
            return allNotes.Where(n => 
                n.Title.ToLowerInvariant().Contains(query) || 
                n.Content.ToLowerInvariant().Contains(query) ||
                n.Tags.Any(t => t.ToLowerInvariant().Contains(query))
            ).ToList();
        }
    }
}