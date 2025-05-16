using Notes.Models;
using Notes.Services.Notes;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

public partial class FoldersPage : ContentPage
{
  private readonly FolderManager _folderManager;
  public ObservableCollection<Folder> Folders { get; } = new ObservableCollection<Folder>();

  public FoldersPage(FolderManager folderManager)
  {
    InitializeComponent();
    _folderManager = folderManager;
    FoldersCollection.ItemsSource = Folders;
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    await LoadFoldersAsync();
  }

  private async Task LoadFoldersAsync()
  {
    Folders.Clear();
    var folders = await _folderManager.GetFoldersAsync(null);
    foreach (var folder in folders)
    {
      Folders.Add(folder);
    }

    if (Folders.Count == 0)
    {
      await CreateDefaultFolderAsync();
    }
  }

  private async Task CreateDefaultFolderAsync()
  {
    var defaultFolder = await _folderManager.CreateFolderAsync("Default");
    Folders.Add(defaultFolder);
  }

  private async void OnAddFolderClicked(object sender, EventArgs e)
  {
    string folderName = await DisplayPromptAsync("New Folder", "Enter folder name:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(folderName))
    {
      var newFolder = await _folderManager.CreateFolderAsync(folderName);
      Folders.Add(newFolder);
    }
  }

  private async void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (e.CurrentSelection.FirstOrDefault() is Folder selectedFolder)
    {
      FoldersCollection.SelectedItem = null;

      var navigationParameter = new Dictionary<string, object>
              {
                  { "FolderId", selectedFolder.Id },
                  { "FolderName", selectedFolder.Name }
              };

      await Shell.Current.GoToAsync(nameof(NotesPage), navigationParameter);
    }
  }
}