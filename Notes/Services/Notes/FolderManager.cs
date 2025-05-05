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
    public class FolderManager
    {
        private readonly FileSystemStorage _storage;
        private readonly NoteRepository _noteRepository;
        private readonly string _foldersFile = "folders.json";
        private List<Folder> _folders;

        public FolderManager(FileSystemStorage storage, NoteRepository noteRepository)
        {
            _storage = storage;
            _noteRepository = noteRepository;
            _folders = new List<Folder>();
            LoadFolders();
        }

        public async Task<Folder> CreateFolderAsync(string name, string parentId)
        {
            var folder = new Folder
            {
                Name = name,
                ParentId = parentId
            };

            _folders.Add(folder);
            await SaveFoldersAsync();
            return folder;
        }

        public async Task DeleteFolderAsync(string folderId)
        {
            // Get the folder
            var folder = GetFolder(folderId);
            if (folder == null)
                return;

            // Get child folders
            var childFolders = GetChildFolders(folderId);
            
            // Delete all notes in this folder
            var notesInFolder = await _noteRepository.GetNotesAsync(folderId);
            foreach (var note in notesInFolder)
            {
                await _noteRepository.DeleteNoteAsync(note.Id);
            }

            // Recursively delete child folders
            foreach (var childFolder in childFolders)
            {
                await DeleteFolderAsync(childFolder.Id);
            }

            // Remove the folder from the list
            _folders.Remove(folder);
            await SaveFoldersAsync();
        }

        public Folder GetFolder(string folderId)
        {
            return _folders.FirstOrDefault(f => f.Id == folderId);
        }

        public List<Folder> GetFolders(string parentId = null)
        {
            if (parentId == null)
                return _folders.Where(f => string.IsNullOrEmpty(f.ParentId)).ToList();
            
            return _folders.Where(f => f.ParentId == parentId).ToList();
        }

        public List<Folder> GetAllFolders()
        {
            return _folders.ToList();
        }

        public List<Folder> GetChildFolders(string folderId)
        {
            return _folders.Where(f => f.ParentId == folderId).ToList();
        }

        public async Task MoveFolderAsync(string folderId, string newParentId)
        {
            var folder = GetFolder(folderId);
            if (folder == null)
                return;

            // Prevent circular references
            if (IsChildFolder(newParentId, folderId))
                throw new InvalidOperationException("Cannot move a folder to its own subfolder");

            folder.ParentId = newParentId;
            await SaveFoldersAsync();
        }

        private bool IsChildFolder(string potentialChildId, string parentId)
        {
            if (string.IsNullOrEmpty(potentialChildId))
                return false;
                
            if (potentialChildId == parentId)
                return true;

            var folder = GetFolder(potentialChildId);
            if (folder == null || string.IsNullOrEmpty(folder.ParentId))
                return false;

            return IsChildFolder(folder.ParentId, parentId);
        }

        private void LoadFolders()
        {
            try
            {
                var foldersData = _storage.ReadFileAsync(_foldersFile).Result;
                if (foldersData != null)
                {
                    _folders = JsonSerializer.Deserialize<List<Folder>>(foldersData);
                }
                else
                {
                    // Initialize with a default root folder if none exists
                    _folders = new List<Folder>
                    {
                        new Folder { Id = "root", Name = "Root", ParentId = null }
                    };
                    SaveFoldersAsync().Wait();
                }
            }
            catch
            {
                _folders = new List<Folder>
                {
                    new Folder { Id = "root", Name = "Root", ParentId = null }
                };
                SaveFoldersAsync().Wait();
            }
        }

        public async Task SaveFoldersAsync()
        {
            var json = JsonSerializer.Serialize(_folders);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _storage.WriteFileAsync(_foldersFile, bytes);
        }
    }
}