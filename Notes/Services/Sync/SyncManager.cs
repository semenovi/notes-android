using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Notes.Models;
using Notes.Services.Notes;
using Notes.Services.Storage;

namespace Notes.Services.Sync
{
    public class SyncManager
    {
        private readonly List<SyncAdapter> _adapters;
        private readonly NoteRepository _noteRepository;
        private readonly FolderManager _folderManager;
        private readonly MediaStorage _mediaStorage;

        public SyncManager(NoteRepository noteRepository, FolderManager folderManager, MediaStorage mediaStorage)
        {
            _adapters = new List<SyncAdapter>();
            _noteRepository = noteRepository;
            _folderManager = folderManager;
            _mediaStorage = mediaStorage;
        }

        public void RegisterAdapter(SyncAdapter adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            if (_adapters.Any(a => a.ProtocolType == adapter.ProtocolType))
                throw new InvalidOperationException($"Adapter for protocol {adapter.ProtocolType} is already registered");

            _adapters.Add(adapter);
        }

        public async Task SynchronizeAsync(SyncProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (!profile.Validate())
                throw new InvalidOperationException("Invalid sync profile");

            var adapter = _adapters.FirstOrDefault(a => a.ProtocolType == profile.Protocol);
            if (adapter == null)
                throw new InvalidOperationException($"No adapter registered for protocol {profile.Protocol}");

            // Connect to the remote endpoint
            await adapter.ConnectAsync(profile);
            if (!adapter.IsConnected)
                throw new InvalidOperationException("Failed to connect to the remote endpoint");

            try
            {
                // Get changes from the remote endpoint
                var remoteChanges = await adapter.GetChangesAsync();
                if (remoteChanges == null || !remoteChanges.Any())
                {
                    // No changes from remote, send our changes
                    var localChanges = await GetLocalChangesAsync();
                    if (localChanges.Any())
                    {
                        await adapter.ApplyChangesAsync(localChanges);
                    }
                }
                else
                {
                    // Got changes from remote, detect conflicts
                    var conflicts = await DetectConflictsAsync(remoteChanges);
                    if (conflicts.Any())
                    {
                        // Handle conflicts
                        foreach (var conflict in conflicts)
                        {
                            await ResolveConflictAsync(conflict);
                        }
                    }

                    // Apply non-conflicting changes
                    var nonConflictingChanges = remoteChanges
                        .Where(c => !conflicts.Any(conflict => conflict.RemoteChange.Id == c.Id))
                        .ToList();

                    foreach (var change in nonConflictingChanges)
                    {
                        await ApplyRemoteChangeAsync(change);
                    }

                    // Send our changes to remote
                    var localChanges = await GetLocalChangesAsync();
                    if (localChanges.Any())
                    {
                        await adapter.ApplyChangesAsync(localChanges);
                    }
                }
            }
            finally
            {
                // Disconnect from the remote endpoint
                await adapter.DisconnectAsync();
            }
        }

        public async Task<List<SyncConflict>> DetectConflictsAsync(List<SyncChange> remoteChanges)
        {
            var conflicts = new List<SyncConflict>();

            foreach (var remoteChange in remoteChanges)
            {
                switch (remoteChange.ItemType)
                {
                    case SyncItemType.Note:
                        var note = await _noteRepository.GetNoteAsync(remoteChange.Id);
                        if (note != null && note.Modified > remoteChange.Timestamp)
                        {
                            conflicts.Add(new SyncConflict
                            {
                                LocalItem = note,
                                RemoteChange = remoteChange,
                                ConflictType = SyncConflictType.Modified
                            });
                        }
                        break;

                    case SyncItemType.Folder:
                        var folder = _folderManager.GetFolder(remoteChange.Id);
                        if (folder != null && remoteChange.ChangeType == SyncChangeType.Deleted)
                        {
                            // Conflict if the folder has been modified locally since last sync
                            conflicts.Add(new SyncConflict
                            {
                                LocalItem = folder,
                                RemoteChange = remoteChange,
                                ConflictType = SyncConflictType.Deleted
                            });
                        }
                        break;

                    case SyncItemType.Media:
                        var media = _mediaStorage.GetMedia(remoteChange.Id);
                        if (media != null && remoteChange.ChangeType == SyncChangeType.Deleted)
                        {
                            // Conflict if the media is still referenced in notes
                            conflicts.Add(new SyncConflict
                            {
                                LocalItem = media,
                                RemoteChange = remoteChange,
                                ConflictType = SyncConflictType.Deleted
                            });
                        }
                        break;
                }
            }

            return conflicts;
        }

        public async Task ResolveConflictAsync(SyncConflict conflict)
        {
            // This is a basic conflict resolution strategy.
            // In a real app, you would prompt the user to choose how to resolve the conflict.

            // For now, we'll use a simple "newest wins" strategy
            if (conflict.RemoteChange.Timestamp > DateTime.MinValue && 
                conflict.LocalItem is Note localNote && 
                conflict.RemoteChange.ItemType == SyncItemType.Note)
            {
                if (conflict.RemoteChange.Timestamp > localNote.Modified)
                {
                    // Remote change is newer, apply it
                    await ApplyRemoteChangeAsync(conflict.RemoteChange);
                }
                // Otherwise, local change is newer, do nothing (will be sent to remote)
            }
            else if (conflict.ConflictType == SyncConflictType.Deleted)
            {
                // For deletion conflicts, favor keeping the item
                // Do nothing
            }
        }

        private async Task<List<SyncChange>> GetLocalChangesAsync()
        {
            var changes = new List<SyncChange>();

            // Get all notes and create change objects
            var notes = await _noteRepository.GetAllNotesAsync();
            foreach (var note in notes)
            {
                changes.Add(new SyncChange
                {
                    Id = note.Id,
                    ItemType = SyncItemType.Note,
                    ChangeType = SyncChangeType.Modified,
                    Timestamp = note.Modified,
                    Data = note
                });
            }

            // Get all folders
            var folders = _folderManager.GetAllFolders();
            foreach (var folder in folders)
            {
                changes.Add(new SyncChange
                {
                    Id = folder.Id,
                    ItemType = SyncItemType.Folder,
                    ChangeType = SyncChangeType.Modified,
                    Data = folder
                });
            }

            // Get all media items
            var mediaItems = _mediaStorage.GetAllMedia();
            foreach (var media in mediaItems)
            {
                changes.Add(new SyncChange
                {
                    Id = media.Id,
                    ItemType = SyncItemType.Media,
                    ChangeType = SyncChangeType.Modified,
                    Timestamp = media.Created,
                    Data = media
                });
            }

            return changes;
        }

        private async Task ApplyRemoteChangeAsync(SyncChange change)
        {
            switch (change.ItemType)
            {
                case SyncItemType.Note:
                    if (change.ChangeType == SyncChangeType.Deleted)
                    {
                        await _noteRepository.DeleteNoteAsync(change.Id);
                    }
                    else if (change.Data is Note note)
                    {
                        await _noteRepository.SaveNoteAsync(note);
                    }
                    break;

                case SyncItemType.Folder:
                    if (change.ChangeType == SyncChangeType.Deleted)
                    {
                        await _folderManager.DeleteFolderAsync(change.Id);
                    }
                    else if (change.Data is Folder folder)
                    {
                        // Check if folder exists
                        var existingFolder = _folderManager.GetFolder(folder.Id);
                        if (existingFolder == null)
                        {
                            await _folderManager.CreateFolderAsync(folder.Name, folder.ParentId);
                        }
                        else
                        {
                            existingFolder.Name = folder.Name;
                            existingFolder.ParentId = folder.ParentId;
                        }
                    }
                    break;

                case SyncItemType.Media:
                    if (change.ChangeType == SyncChangeType.Deleted)
                    {
                        _mediaStorage.DeleteMedia(change.Id);
                    }
                    else if (change.Data is MediaItem media && change.BinaryData != null)
                    {
                        await media.SaveContentAsync(change.BinaryData);
                    }
                    break;
            }
        }
    }
}