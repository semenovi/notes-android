using Notes.Models;
using Notes.Services;

namespace Notes.Views.Windows;

public partial class MainWindow : ContentPage
{
  private readonly ToastService _toastService;

  public MainWindow()
  {
    InitializeComponent();
    _toastService = IPlatformApplication.Current.Services.GetRequiredService<ToastService>();
    _toastService.ToastRequested += OnToastRequested;
  }

  private void OnToastRequested(string message)
    => Toast.ShowToast(message);

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
