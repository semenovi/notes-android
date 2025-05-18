using Notes.Models;
using Notes.Services.Notes;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

[QueryProperty(nameof(FolderId), "FolderId")]
[QueryProperty(nameof(FolderName), "FolderName")]
public partial class NotesPage : ContentPage
{
  private readonly NoteManager _noteManager;
  private readonly FolderManager _folderManager;
  public ObservableCollection<Note> Notes { get; } = new ObservableCollection<Note>();

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

  public NotesPage(NoteManager noteManager, FolderManager folderManager)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _folderManager = folderManager;
    NotesCollection.ItemsSource = Notes;
    BindingContext = this;
  }

  private async Task LoadNotesAsync()
  {
    if (string.IsNullOrEmpty(FolderId))
      return;

    Notes.Clear();
    var notes = await _noteManager.GetNotesAsync(FolderId);
    foreach (var note in notes)
    {
      Notes.Add(note);
    }
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

  private async void OnDeleteFolderClicked(object sender, EventArgs e)
  {
    bool confirm = await DisplayAlert("Confirm Delete", $"Are you sure you want to delete folder '{FolderName}'?", "Yes", "No");
    if (confirm)
    {
      await _folderManager.DeleteFolderAsync(FolderId);
      await Shell.Current.GoToAsync("..");
    }
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