using Notes.Models;
using Notes.Services.Export;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

public partial class FoldersPage : ContentPage
{
  private readonly FolderManager _folderManager;
  private readonly ExportService _exportService;
  private readonly SyncManager _syncManager;
  private readonly SyncSettingsService _syncSettingsService;
  public ObservableCollection<Folder> Folders { get; } = new ObservableCollection<Folder>();

  public FoldersPage(FolderManager folderManager, ExportService exportService,
      SyncManager syncManager, SyncSettingsService syncSettingsService)
  {
    InitializeComponent();
    _folderManager = folderManager;
    _exportService = exportService;
    _syncManager = syncManager;
    _syncSettingsService = syncSettingsService;
    FoldersCollection.ItemsSource = Folders;
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    await UpdateSyncToggleTextAsync();
    await LoadFoldersAsync();
    await AutoSyncAsync();
  }

  private async Task UpdateSyncToggleTextAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();
    SyncToggleItem.Text = settings.Enabled ? "Sync: ON" : "Sync: OFF";
  }

  private async Task AutoSyncAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled) return;
    await RunSyncAsync();
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

  private async void OnSyncToggleClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    settings.Enabled = !settings.Enabled;
    await _syncSettingsService.SaveAsync(settings);
    SyncToggleItem.Text = settings.Enabled ? "Sync: ON" : "Sync: OFF";

    if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
      await ShowSyncSettingsDialogAsync();
  }

  private async void OnSyncNowClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled)
    {
      await DisplayAlert("Sync", "Enable sync first.", "OK");
      return;
    }
    await RunSyncAsync();
    await LoadFoldersAsync();
  }

  private async void OnSyncSettingsClicked(object sender, EventArgs e)
  {
    await ShowSyncSettingsDialogAsync();
  }

  private async Task ShowSyncSettingsDialogAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();

    string? url = await DisplayPromptAsync("Sync Settings", "Server URL:",
        initialValue: settings.ServerUrl, placeholder: "http://46.148.142.210:8080");
    if (url == null) return;

    string? token = await DisplayPromptAsync("Sync Settings", "API Token (from /api/sync/setup on server):",
        initialValue: settings.ApiToken, placeholder: "paste token here");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();

    await _syncSettingsService.SaveAsync(settings);
    await DisplayAlert("Sync Settings", "Settings saved.", "OK");
  }

  private async Task RunSyncAsync()
  {
    try
    {
      var profile = new Notes.Models.SyncProfile
      {
        Name = "Network",
        Protocol = Notes.Models.SyncProtocolType.Network,
      };
      await _syncManager.SynchronizeAsync(profile);
    }
    catch (Exception ex)
    {
      await DisplayAlert("Sync Error", ex.Message, "OK");
    }
  }

  private async void OnExportBackupClicked(object sender, EventArgs e)
  {
    try
    {
      string result = await _exportService.ExportBackupAsync();
      await DisplayAlert("Success", "Backup exported successfully.", "OK");
    }
    catch (Exception ex)
    {
      await DisplayAlert("Error", ex.Message, "OK");
    }
  }

  private async void OnImportBackupClicked(object sender, EventArgs e)
  {
    await ImportBackupAsync();
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

  private async Task ImportBackupAsync()
  {
    bool confirmImport = await DisplayAlert("Confirmation",
        "Import will replace all existing data. Continue?", "Yes", "No");

    if (!confirmImport)
      return;

    try
    {
      var fileResult = await FilePicker.PickAsync(new PickOptions
      {
        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
          { DevicePlatform.iOS, new[] { "public.zip-archive" } },
          { DevicePlatform.Android, new[] { "application/zip" } },
          { DevicePlatform.WinUI, new[] { ".zip" } },
          { DevicePlatform.macOS, new[] { "zip" } }
        }),
        PickerTitle = "Select backup file"
      });

      if (fileResult == null)
        return;

      await DisplayAlert("Information", "Starting import process...", "OK");

      string tempPath = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(fileResult.FullPath));
      using (var sourceStream = await fileResult.OpenReadAsync())
      using (var destStream = File.Create(tempPath))
      {
        await sourceStream.CopyToAsync(destStream);
      }

      await _exportService.ImportBackupAsync(tempPath);

      await DisplayAlert("Success", "Backup imported successfully. The app data has been replaced.", "OK");

      await LoadFoldersAsync();
    }
    catch (Exception ex)
    {
      await DisplayAlert("Error", $"Failed to import backup: {ex.Message}", "OK");
    }
  }
}