using Notes.Models;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

[QueryProperty(nameof(FolderId), "FolderId")]
[QueryProperty(nameof(FolderName), "FolderName")]
public partial class NotesPage : ContentPage
{
  private readonly NoteManager _noteManager;
  private readonly FolderManager _folderManager;
  private readonly ReactiveSyncService _reactiveSync;
  public ObservableCollection<Note> Notes { get; } = new ObservableCollection<Note>();
  private CancellationTokenSource? _loadCts;

  private string _folderId;
  public string FolderId
  {
    get => _folderId;
    set
    {
      _folderId = value;
      LoadNotesAsync().ConfigureAwait(false);
    }
  }

  private string _folderName;
  public string FolderName
  {
    get => _folderName;
    set
    {
      _folderName = value;
      OnPropertyChanged();
    }
  }

  public NotesPage(NoteManager noteManager, FolderManager folderManager, ReactiveSyncService reactiveSync)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _folderManager = folderManager;
    _reactiveSync = reactiveSync;
    NotesCollection.ItemsSource = Notes;
    BindingContext = this;
  }

  protected override void OnAppearing()
  {
    base.OnAppearing();
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    _reactiveSync.RemoteChangesApplied -= OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied() => await LoadNotesAsync();

  private async Task LoadNotesAsync()
  {
    if (string.IsNullOrEmpty(FolderId))
      return;
    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var notes = await _noteManager.GetNotesAsync(FolderId);
    if (cts.IsCancellationRequested) return;
    Notes.Clear();
    foreach (var note in notes)
      Notes.Add(note);
  }

  private async void OnAddNoteClicked(object sender, EventArgs e)
  {
    string noteTitle = await DisplayPromptAsync("New Note", "Enter note title:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(noteTitle))
    {
      var newNote = await _noteManager.CreateNoteAsync(noteTitle, FolderId);
      Notes.Add(newNote);

      await NavigateToNoteEditor(newNote);
    }
  }

  private async void OnNoteSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (e.CurrentSelection.FirstOrDefault() is Note selectedNote)
    {
      NotesCollection.SelectedItem = null;

      await NavigateToNoteView(selectedNote);
    }
  }

  private async void OnRenameFolderClicked(object sender, EventArgs e)
  {
    var folder = await _folderManager.GetFolderAsync(FolderId);
    if (folder == null) return;

    var newName = await DisplayPromptAsync("Переименовать папку", "Новое название:", initialValue: folder.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == folder.Name) return;

    folder.Name = newName;
    folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(folder);
    FolderName = newName;
  }

  private async void OnDeleteFolderClicked(object sender, EventArgs e)
  {
    bool confirm = await DisplayAlert("Удалить папку",
        $"Удалить «{FolderName}» и все заметки в ней?", "Удалить", "Отмена");
    if (!confirm) return;

    var notes = await _noteManager.GetNotesAsync(FolderId);
    foreach (var note in notes)
      await _noteManager.DeleteNoteAsync(note.Id);

    await _folderManager.DeleteFolderAsync(FolderId);
    await Shell.Current.GoToAsync("..");
  }

  private async Task NavigateToNoteView(Note note)
  {
    var navigationParameter = new Dictionary<string, object>
    {
      { "NoteId", note.Id }
    };

    await Shell.Current.GoToAsync(nameof(NoteViewPage), navigationParameter);
  }

  private async Task NavigateToNoteEditor(Note note)
  {
    var navigationParameter = new Dictionary<string, object>
    {
      { "NoteId", note.Id }
    };

    await Shell.Current.GoToAsync(nameof(NoteEditorPage), navigationParameter);
  }
}