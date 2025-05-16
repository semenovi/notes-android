using Notes.Models;

namespace Notes.Views.Pages;

public partial class NotesPage : ContentPage
{
  public NotesPage()
  {
    InitializeComponent();

    FolderTree.FolderSelected += OnFolderSelected;
    NoteList.NoteSelected += OnNoteSelected;
    Editor.NoteSaved += OnNoteSaved;
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    await InitializeAppDataAsync();
  }

  private async Task InitializeAppDataAsync()
  {
    await FolderTree.LoadFoldersAsync();
  }

  private async void OnFolderSelected(object sender, Folder folder)
  {
    await NoteList.LoadNotesAsync(folder.Id);
  }

  private void OnNoteSelected(object sender, Note note)
  {
    Editor.LoadNote(note);
  }

  private void OnNoteSaved(object sender, EventArgs e)
  {
    if (Editor.BindingContext is Note note)
    {
      NoteList.AddOrUpdateNote(note);
    }
  }
}