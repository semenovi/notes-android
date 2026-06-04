using CommunityToolkit.Maui.Storage;
using Notes.Models;
using Notes.Services;
using Notes.Services.Export;
using Notes.Services.Notes;
using Notes.Services.Sync;
using Notes.Views.Controls;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

public partial class FoldersPage : ContentPage
{
  private readonly FolderManager _folderManager;
  private readonly NoteManager _noteManager;
  private readonly ExportService _exportService;
  private readonly SyncManager _syncManager;
  private readonly SyncSettingsService _syncSettingsService;
  private readonly ReactiveSyncService _reactiveSync;
  private readonly ProgressNotificationService _progressService;
  public ObservableCollection<Folder> Folders { get; } = new ObservableCollection<Folder>();
  private CancellationTokenSource? _loadCts;

  public FoldersPage(FolderManager folderManager, NoteManager noteManager,
      ExportService exportService, SyncManager syncManager,
      SyncSettingsService syncSettingsService, ReactiveSyncService reactiveSync,
      ProgressNotificationService progressService)
  {
    InitializeComponent();
    _folderManager = folderManager;
    _noteManager = noteManager;
    _exportService = exportService;
    _syncManager = syncManager;
    _syncSettingsService = syncSettingsService;
    _reactiveSync = reactiveSync;
    _progressService = progressService;
    FoldersCollection.ItemsSource = Folders;

    var exportLogsItem = new ToolbarItem { Text = "Export Logs", Order = ToolbarItemOrder.Secondary };
    exportLogsItem.Clicked += OnExportLogsClicked;
    ToolbarItems.Add(exportLogsItem);
  }

  protected override async void OnAppearing()
  {
    base.OnAppearing();
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
    _progressService.ShowRequested += PageProgress.ShowProgress;
    _progressService.UpdateRequested += PageProgress.UpdateProgress;
    _progressService.HideRequested += PageProgress.HideProgress;
    if (_progressService.Current != null)
      PageProgress.ShowProgress(_progressService.Current);
    await UpdateSyncToggleTextAsync();
    await LoadFoldersAsync();
    _ = AutoSyncAsync();
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    _reactiveSync.RemoteChangesApplied -= OnRemoteChangesApplied;
    _progressService.ShowRequested -= PageProgress.ShowProgress;
    _progressService.UpdateRequested -= PageProgress.UpdateProgress;
    _progressService.HideRequested -= PageProgress.HideProgress;
    PageProgress.Reset();
  }

  private async void OnRemoteChangesApplied() => await LoadFoldersAsync();

  private async Task UpdateSyncToggleTextAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();
    SyncToggleItem.Text = settings.Enabled ? "Sync: ON" : "Sync: OFF";
  }

  private async Task AutoSyncAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (settings.Enabled)
    {
      await RunSyncAsync();
      await LoadFoldersAsync();
    }
  }

  private async Task LoadFoldersAsync()
  {
    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var folders = await _folderManager.GetFoldersAsync(null);
    if (cts.IsCancellationRequested) return;
    Folders.Clear();
    foreach (var folder in folders)
      Folders.Add(folder);
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
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    await DisplayAlert("Sync Settings", "Settings saved.", "OK");

    await _reactiveSync.RestartAsync();
    await RunSyncAsync();
    await LoadFoldersAsync();
  }

  private async Task RunSyncAsync()
  {
    using var session = _progressService.Begin("Syncing");
    try
    {
      await Task.Run(() => _syncManager.SynchronizeAsync(new Notes.Models.SyncProfile
      {
        Name = "Network",
        Protocol = Notes.Models.SyncProtocolType.Network,
      }, session.Report));
    }
    catch (InvalidOperationException ex)
    {
      await DisplayAlert("Sync Error", ex.Message, "OK");
    }
    catch (Exception ex)
    {
      await DisplayAlert("Sync Error", ex.GetType().Name + ": " + ex.Message, "OK");
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

  private async void OnFolderTapped(object sender, TappedEventArgs e)
  {
    if (sender is View view && view.BindingContext is Folder folder)
    {
      await view.ScaleTo(0.96, 80);
      await view.ScaleTo(1.0, 80);
      await Shell.Current.GoToAsync(nameof(NotesPage), new Dictionary<string, object>
      {
        { "FolderId", folder.Id },
        { "FolderName", folder.Name }
      });
    }
  }

  private async Task DeleteFolderWithContentsAsync(Folder folder)
  {
    bool confirm = await DisplayAlert("Delete Folder",
        $"Delete \"{folder.Name}\" and all notes inside?", "Delete", "Cancel");
    if (!confirm) return;

    var notes = await _noteManager.GetNotesAsync(folder.Id);
    foreach (var note in notes)
      await _noteManager.DeleteNoteAsync(note.Id);

    await _folderManager.DeleteFolderAsync(folder.Id);
    Folders.Remove(folder);
  }

  private async void OnExportLogsClicked(object sender, EventArgs e)
  {
    var log = DebugLogService.Current;
    if (log == null) { await DisplayAlert("Logs", "Log service not initialized.", "OK"); return; }
    var text = log.GetLogsText();
    if (string.IsNullOrEmpty(text)) { await DisplayAlert("Logs", "No log entries yet.", "OK"); return; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    using var stream = new MemoryStream(bytes);
    var fileName = $"notes_debug_{DateTime.Now:yyyyMMddHHmmss}.log";
    var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
    if (!result.IsSuccessful)
      await DisplayAlert("Error", result.Exception?.Message ?? "Save failed", "OK");
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