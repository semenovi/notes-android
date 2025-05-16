using Notes.Models;
using Notes.Services.Notes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Controls;

public partial class NoteListView : ContentView
{
  private readonly NoteManager _noteManager;

  public ObservableCollection<NoteViewModel> Notes { get; private set; } = new ObservableCollection<NoteViewModel>();

  public event EventHandler<Note> NoteSelected;

  private NoteViewModel _selectedNote;

  public NoteViewModel SelectedNote
  {
    get => _selectedNote;
    set
    {
      if (_selectedNote != value)
      {
        if (_selectedNote != null)
          _selectedNote.IsSelected = false;

        _selectedNote = value;

        if (_selectedNote != null)
          _selectedNote.IsSelected = true;

        OnPropertyChanged();
        IsDeleteEnabled = _selectedNote != null;

        if (_selectedNote != null)
          NoteSelected?.Invoke(this, _selectedNote.Note);
      }
    }
  }

  private bool _isDeleteEnabled;

  public bool IsDeleteEnabled
  {
    get => _isDeleteEnabled;
    set
    {
      if (_isDeleteEnabled != value)
      {
        _isDeleteEnabled = value;
        OnPropertyChanged();
      }
    }
  }

  private string _currentFolderId;

  public NoteListView()
  {
    InitializeComponent();
    _noteManager = App.Current.Handler.MauiContext.Services.GetService<NoteManager>();
    BindingContext = this;
    NotesCollectionView.ItemsSource = Notes;
  }

  public async Task LoadNotesAsync(string folderId)
  {
    _currentFolderId = folderId;
    Notes.Clear();
    var notes = await _noteManager.GetNotesAsync(folderId);

    foreach (var note in notes)
    {
      Notes.Add(new NoteViewModel(note));
    }

    if (Notes.Count > 0)
      SelectedNote = Notes[0];
    else
      SelectedNote = null;
  }

  private async void OnAddNoteClicked(object sender, EventArgs e)
  {
    if (string.IsNullOrEmpty(_currentFolderId))
      return;

    string noteTitle = await Application.Current.MainPage.DisplayPromptAsync("New Note", "Enter note title:");

    if (!string.IsNullOrEmpty(noteTitle))
    {
      var note = await _noteManager.CreateNoteAsync(noteTitle, _currentFolderId);
      var viewModel = new NoteViewModel(note);
      Notes.Add(viewModel);
      SelectedNote = viewModel;
    }
  }

  private async void OnDeleteNoteClicked(object sender, EventArgs e)
  {
    if (SelectedNote == null)
      return;

    bool confirm = await Application.Current.MainPage.DisplayAlert("Confirm Delete",
        $"Are you sure you want to delete note '{SelectedNote.Title}'?", "Yes", "No");

    if (confirm)
    {
      await _noteManager.DeleteNoteAsync(SelectedNote.Note.Id);
      Notes.Remove(SelectedNote);

      if (Notes.Count > 0)
        SelectedNote = Notes[0];
      else
        SelectedNote = null;
    }
  }

  private void OnNoteSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (e.CurrentSelection.FirstOrDefault() is NoteViewModel selectedNote)
    {
      SelectedNote = selectedNote;
    }
  }

  public void AddOrUpdateNote(Note note)
  {
    if (note.FolderId != _currentFolderId)
      return;

    var existingNoteVm = Notes.FirstOrDefault(n => n.Note.Id == note.Id);

    if (existingNoteVm != null)
    {
      var index = Notes.IndexOf(existingNoteVm);
      Notes[index] = new NoteViewModel(note);

      if (SelectedNote?.Note.Id == note.Id)
        SelectedNote = Notes[index];
    }
    else
    {
      var newNoteVm = new NoteViewModel(note);
      Notes.Add(newNoteVm);

      if (Notes.Count == 1)
        SelectedNote = newNoteVm;
    }
  }
}

public class NoteViewModel : INotifyPropertyChanged
{
  public Note Note { get; }

  public string Title => Note.Title;
  public DateTime Modified => Note.Modified;

  private bool _isSelected;

  public bool IsSelected
  {
    get => _isSelected;
    set
    {
      if (_isSelected != value)
      {
        _isSelected = value;
        OnPropertyChanged();
      }
    }
  }

  public NoteViewModel(Note note)
  {
    Note = note;
  }

  public event PropertyChangedEventHandler PropertyChanged;

  protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}