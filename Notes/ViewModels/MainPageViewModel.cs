using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Notes.Models;
using Notes.Services.Notes;
using Notes.Services.Storage;

namespace Notes.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private readonly NoteRepository _noteRepository;
        private readonly FolderManager _folderManager;
        private readonly MediaStorage _mediaStorage;

        private Folder _selectedFolder;
        private Note _selectedNote;
        private string _searchText;
        private ObservableCollection<Folder> _folders;
        private ObservableCollection<Note> _notes;

        public ObservableCollection<Folder> Folders
        {
            get => _folders;
            set => SetProperty(ref _folders, value);
        }

        public ObservableCollection<Note> Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public Folder SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (SetProperty(ref _selectedFolder, value))
                {
                    LoadNotes();
                    OnPropertyChanged(nameof(IsFolderSelected));
                }
            }
        }

        public Note SelectedNote
        {
            get => _selectedNote;
            set
            {
                if (SetProperty(ref _selectedNote, value))
                {
                    OnPropertyChanged(nameof(IsNoteSelected));
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    LoadNotes();
                }
            }
        }

        public bool IsFolderSelected => SelectedFolder != null;
        public bool IsNoteSelected => SelectedNote != null;

        public ICommand NewNoteCommand { get; }
        public ICommand NewFolderCommand { get; }
        public ICommand DeleteFolderCommand { get; }
        public ICommand RenameFolderCommand { get; }
        public ICommand DeleteNoteCommand { get; }
        public ICommand FormatTextCommand { get; }
        public ICommand AddMediaCommand { get; }
        public ICommand SyncCommand { get; }
        public ICommand SettingsCommand { get; }

        public MainPageViewModel()
        {
            // In a real app, these would be injected via dependency injection
            var storage = new FileSystemStorage("Notes");
            _noteRepository = new NoteRepository(storage);
            _folderManager = new FolderManager(storage, _noteRepository);
            _mediaStorage = new MediaStorage(storage);

            Folders = new ObservableCollection<Folder>();
            Notes = new ObservableCollection<Note>();

            NewNoteCommand = new Command(async () => await CreateNewNote());
            NewFolderCommand = new Command(async () => await CreateNewFolder());
            DeleteFolderCommand = new Command(async () => await DeleteFolder(), () => IsFolderSelected);
            RenameFolderCommand = new Command(async () => await RenameFolder(), () => IsFolderSelected);
            DeleteNoteCommand = new Command(async () => await DeleteNote(), () => IsNoteSelected);
            FormatTextCommand = new Command<string>(ApplyTextFormatting);
            AddMediaCommand = new Command(async () => await AddMedia());
            SyncCommand = new Command(async () => await Sync());
            SettingsCommand = new Command(async () => await OpenSettings());
        }

        public async void Initialize()
        {
            await LoadFolders();
            if (Folders.Any())
            {
                SelectedFolder = Folders.First();
            }
        }

        private async Task LoadFolders()
        {
            var allFolders = _folderManager.GetAllFolders();
            Folders.Clear();
            foreach (var folder in allFolders)
            {
                Folders.Add(folder);
            }
        }

        private async Task LoadNotes()
        {
            if (SelectedFolder == null)
                return;

            IEnumerable<Note> notes;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                notes = await _noteRepository.SearchNotesAsync(SearchText);
                notes = notes.Where(n => n.FolderId == SelectedFolder.Id);
            }
            else
            {
                notes = await _noteRepository.GetNotesAsync(SelectedFolder.Id);
            }

            Notes.Clear();
            foreach (var note in notes.OrderByDescending(n => n.Modified))
            {
                Notes.Add(note);
            }

            SelectedNote = Notes.FirstOrDefault();
        }

        private async Task CreateNewNote()
        {
            if (SelectedFolder == null)
                return;

            var note = new Note
            {
                Title = "New Note",
                Content = "",
                FolderId = SelectedFolder.Id
            };

            await _noteRepository.SaveNoteAsync(note);
            Notes.Insert(0, note);
            SelectedNote = note;
        }

        private async Task CreateNewFolder()
        {
            string folderName = await Application.Current.MainPage.DisplayPromptAsync(
                "New Folder", 
                "Enter folder name:", 
                "Create", 
                "Cancel");

            if (!string.IsNullOrWhiteSpace(folderName))
            {
                var parentId = SelectedFolder?.Id;
                var folder = await _folderManager.CreateFolderAsync(folderName, parentId);
                Folders.Add(folder);
                SelectedFolder = folder;
            }
        }

        private async Task DeleteFolder()
        {
            if (SelectedFolder == null)
                return;

            bool confirm = await Application.Current.MainPage.DisplayAlert(
                "Delete Folder",
                $"Are you sure you want to delete folder '{SelectedFolder.Name}' and all its contents?",
                "Delete",
                "Cancel");

            if (confirm)
            {
                var folderId = SelectedFolder.Id;
                await _folderManager.DeleteFolderAsync(folderId);
                
                var folderToRemove = Folders.FirstOrDefault(f => f.Id == folderId);
                if (folderToRemove != null)
                {
                    Folders.Remove(folderToRemove);
                }

                SelectedFolder = Folders.FirstOrDefault();
            }
        }

        private async Task RenameFolder()
        {
            if (SelectedFolder == null)
                return;

            string newName = await Application.Current.MainPage.DisplayPromptAsync(
                "Rename Folder",
                "Enter new name:",
                "Rename",
                "Cancel",
                initialValue: SelectedFolder.Name);

            if (!string.IsNullOrWhiteSpace(newName))
            {
                SelectedFolder.Name = newName;
                // Update folder in storage
                await _folderManager.SaveFoldersAsync();
            }
        }

        private async Task DeleteNote()
        {
            if (SelectedNote == null)
                return;

            bool confirm = await Application.Current.MainPage.DisplayAlert(
                "Delete Note",
                $"Are you sure you want to delete note '{SelectedNote.Title}'?",
                "Delete",
                "Cancel");

            if (confirm)
            {
                var noteId = SelectedNote.Id;
                await _noteRepository.DeleteNoteAsync(noteId);
                
                var noteToRemove = Notes.FirstOrDefault(n => n.Id == noteId);
                if (noteToRemove != null)
                {
                    Notes.Remove(noteToRemove);
                }

                SelectedNote = Notes.FirstOrDefault();
            }
        }

        private void ApplyTextFormatting(string format)
        {
            if (SelectedNote == null)
                return;

            // This is a simplified implementation
            // In a real app, you would need to handle text selection
            switch (format.ToLower())
            {
                case "bold":
                    SelectedNote.Content += "**Bold Text**";
                    break;
                case "italic":
                    SelectedNote.Content += "*Italic Text*";
                    break;
                case "link":
                    SelectedNote.Content += "[Link Text](https://example.com)";
                    break;
            }

            // Save the note
            _noteRepository.SaveNoteAsync(SelectedNote).ConfigureAwait(false);
        }

        private async Task AddMedia()
        {
            if (SelectedNote == null)
                return;

            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Media File",
                    FileTypes = FilePickerFileType.Images
                });

                if (result != null)
                {
                    var mediaItem = await _mediaStorage.AddMediaAsync(result.FullPath);
                    if (mediaItem != null)
                    {
                        // Add a Markdown link to the media in the note content
                        SelectedNote.Content += $"\n\n![{Path.GetFileNameWithoutExtension(mediaItem.FileName)}](media:{mediaItem.Id})";
                        
                        // Save the note
                        await _noteRepository.SaveNoteAsync(SelectedNote);
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    $"Failed to add media: {ex.Message}",
                    "OK");
            }
        }

        private async Task Sync()
        {
            await Application.Current.MainPage.DisplayAlert(
                "Sync",
                "Syncing not yet implemented in this prototype.",
                "OK");
        }

        private async Task OpenSettings()
        {
            await Application.Current.MainPage.DisplayAlert(
                "Settings",
                "Settings not yet implemented in this prototype.",
                "OK");
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T backingStore, T value,
            [CallerMemberName] string propertyName = "",
            Action onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            onChanged?.Invoke();
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}