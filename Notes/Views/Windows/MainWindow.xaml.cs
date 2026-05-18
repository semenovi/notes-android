using Notes.Models;

namespace Notes.Views.Windows;

public partial class MainWindow : ContentPage
{
  public MainWindow()
  {
    InitializeComponent();
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    await FolderTree.LoadFoldersAsync();
    await FolderTree.SyncIfEnabledAsync();
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

  private void OnNoteDeleted(object sender, Note note)
  {
    NoteEditor.ClearEditor();
  }
}
