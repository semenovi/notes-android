using Notes.Models;
using Notes.Services.Notes;
using Notes.Services.Export;

namespace Notes.Views.Windows;

public partial class MainWindow : ContentPage
{
  private readonly NoteManager _noteManager;

  public MainWindow(FolderManager folderManager, NoteManager noteManager, ExportService exportService)
  {
    InitializeComponent();
    _noteManager = noteManager;
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    await FolderTree.LoadFoldersAsync();
  }

  private async void OnFolderSelected(object sender, Folder folder)
  {
    NotesList.SetFolderName(folder.Name);
    await NotesList.LoadNotesAsync(folder.Id);
  }

  private async void OnNoteSelected(object sender, Note note)
  {
    await NoteEditor.LoadNoteAsync(note);
  }

  private async void OnDeleteNoteRequested(object sender, Note note)
  {
    var confirm = await DisplayAlert("Удалить заметку",
        $"Удалить «{note.Title}»?", "Удалить", "Отмена");

    if (confirm)
    {
      await _noteManager.DeleteNoteAsync(note.Id);
      NotesList.RemoveNote(note.Id);
      NoteEditor.ClearEditor();
    }
  }
}
