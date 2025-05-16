using Notes.Models;
using Notes.Services.Notes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Controls;

public partial class FolderTreeView : ContentView
{
  private readonly FolderManager _folderManager;

  public ObservableCollection<FolderViewModel> Folders { get; private set; } = new ObservableCollection<FolderViewModel>();

  public event EventHandler<Folder> FolderSelected;

  private FolderViewModel _selectedFolder;

  public FolderViewModel SelectedFolder
  {
    get => _selectedFolder;
    set
    {
      if (_selectedFolder != value)
      {
        if (_selectedFolder != null)
          _selectedFolder.IsSelected = false;

        _selectedFolder = value;

        if (_selectedFolder != null)
          _selectedFolder.IsSelected = true;

        OnPropertyChanged();
        IsDeleteEnabled = _selectedFolder != null;

        if (_selectedFolder != null)
          FolderSelected?.Invoke(this, _selectedFolder.Folder);
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

  public FolderTreeView()
  {
    InitializeComponent();
    _folderManager = App.Current.Handler.MauiContext.Services.GetService<FolderManager>();
    BindingContext = this;
    FoldersCollectionView.ItemsSource = Folders;
  }

  public async Task LoadFoldersAsync()
  {
    Folders.Clear();
    var folders = await _folderManager.GetAllFoldersAsync();

    foreach (var folder in folders)
    {
      Folders.Add(new FolderViewModel(folder));
    }

    if (Folders.Count > 0)
      SelectedFolder = Folders[0];
  }

  private async void OnAddFolderClicked(object sender, EventArgs e)
  {
    string folderName = await Application.Current.MainPage.DisplayPromptAsync("New Folder", "Enter folder name:");

    if (!string.IsNullOrEmpty(folderName))
    {
      var folder = await _folderManager.CreateFolderAsync(folderName);
      var viewModel = new FolderViewModel(folder);
      Folders.Add(viewModel);
      SelectedFolder = viewModel;
    }
  }

  private async void OnDeleteFolderClicked(object sender, EventArgs e)
  {
    if (SelectedFolder == null)
      return;

    bool confirm = await Application.Current.MainPage.DisplayAlert("Confirm Delete",
        $"Are you sure you want to delete folder '{SelectedFolder.Name}'?", "Yes", "No");

    if (confirm)
    {
      await _folderManager.DeleteFolderAsync(SelectedFolder.Folder.Id);
      Folders.Remove(SelectedFolder);

      if (Folders.Count > 0)
        SelectedFolder = Folders[0];
      else
        SelectedFolder = null;
    }
  }

  private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (e.CurrentSelection.FirstOrDefault() is FolderViewModel selectedFolder)
    {
      SelectedFolder = selectedFolder;
    }
  }
}

public class FolderViewModel : INotifyPropertyChanged
{
  public Folder Folder { get; }

  public string Name => Folder.Name;

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

  public FolderViewModel(Folder folder)
  {
    Folder = folder;
  }

  public event PropertyChangedEventHandler PropertyChanged;

  protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}