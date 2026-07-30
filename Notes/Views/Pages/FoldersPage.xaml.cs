using CommunityToolkit.Maui.Storage;
using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Export;
using Notes.Services.Notes;
using Notes.Services.Sync;
using Notes.Views.Controls;
using System.Collections.ObjectModel;
using System.Linq;

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
  private readonly ToastService _toastService;
  public ObservableCollection<Folder> Folders { get; } = new ObservableCollection<Folder>();
  private CancellationTokenSource? _loadCts;

  public FoldersPage(FolderManager folderManager, NoteManager noteManager,
      ExportService exportService, SyncManager syncManager,
      SyncSettingsService syncSettingsService, ReactiveSyncService reactiveSync,
      ProgressNotificationService progressService, ToastService toastService)
  {
    InitializeComponent();
    _folderManager = folderManager;
    _noteManager = noteManager;
    _exportService = exportService;
    _syncManager = syncManager;
    _syncSettingsService = syncSettingsService;
    _reactiveSync = reactiveSync;
    _progressService = progressService;
    _toastService = toastService;
    FoldersCollection.ItemsSource = Folders;

    var exportLogsItem = new ToolbarItem { Text = "export logs", Order = ToolbarItemOrder.Secondary };
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
    await Task.WhenAll(UpdateSyncToggleTextAsync(), LoadFoldersAsync());
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
    SyncToggleItem.Text = settings.Enabled ? "sync: on" : "sync: off";
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
    var sorted = folders.OrderBy(f => f.Name, NaturalSortComparer.Instance).ToList();
    DiffUpdateFolders(sorted);
  }

  private void DiffUpdateFolders(List<Folder> newFolders)
  {
    var newById = newFolders.ToDictionary(f => f.Id);

    for (int i = Folders.Count - 1; i >= 0; i--)
      if (!newById.ContainsKey(Folders[i].Id))
        Folders.RemoveAt(i);

    foreach (var folder in newFolders)
    {
      int idx = -1;
      for (int i = 0; i < Folders.Count; i++)
        if (Folders[i].Id == folder.Id) { idx = i; break; }

      if (idx < 0)
        Folders.Add(folder);
      else if (Folders[idx].Modified != folder.Modified)
        Folders[idx] = folder;
    }
  }

  private async void OnAddFolderClicked(object sender, EventArgs e)
  {
    string folderName = await DisplayPromptAsync("new folder", "enter folder name:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(folderName))
    {
      await _folderManager.CreateFolderAsync(folderName);
      await LoadFoldersAsync();
    }
  }

  private async void OnSyncToggleClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    settings.Enabled = !settings.Enabled;
    await _syncSettingsService.SaveAsync(settings);
    SyncToggleItem.Text = settings.Enabled ? "sync: on" : "sync: off";

    if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
      await ShowSyncSettingsDialogAsync();
  }

  private async void OnSyncNowClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled)
    {
      await DisplayAlert("sync", "enable sync first", "ok");
      return;
    }
    int applied = await RunSyncAsync();
    await LoadFoldersAsync();
    if (applied >= 0)
      _toastService.Show(applied > 0
          ? $"sync complete: {applied} {(applied == 1 ? "change" : "changes")} applied"
          : "sync complete, no changes");
  }

  private async void OnSyncSettingsClicked(object sender, EventArgs e)
  {
    await ShowSyncSettingsDialogAsync();
  }

  private async Task ShowSyncSettingsDialogAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();

    string? url = await DisplayPromptAsync("sync settings", "server url:",
        initialValue: settings.ServerUrl, placeholder: "http://46.148.142.210:8080");
    if (url == null) return;

    string? token = await DisplayPromptAsync("sync settings", "api token (from /api/sync/setup on server):",
        initialValue: settings.ApiToken, placeholder: "paste token here");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    await DisplayAlert("sync settings", "settings saved", "ok");

    await _reactiveSync.RestartAsync();
    await RunSyncAsync();
    await LoadFoldersAsync();
  }

  // Returns the number of remote changes applied, or -1 if the sync failed.
  private async Task<int> RunSyncAsync()
  {
    using var session = _progressService.Begin("syncing");
    try
    {
      return await Task.Run(() => _syncManager.SynchronizeAsync(new Notes.Models.SyncProfile
      {
        Name = "Network",
        Protocol = Notes.Models.SyncProtocolType.Network,
      }, session.Report));
    }
    catch (InvalidOperationException ex)
    {
      await DisplayAlert("sync error", ex.Message, "ok");
    }
    catch (Exception ex)
    {
      await DisplayAlert("sync error", ex.GetType().Name + ": " + ex.Message, "ok");
    }
    return -1;
  }

  private async void OnExportBackupClicked(object sender, EventArgs e)
  {
    try
    {
      string result = await _exportService.ExportBackupAsync();
      await DisplayAlert("success", "backup exported successfully", "ok");
    }
    catch (Exception ex)
    {
      await DisplayAlert("error", ex.Message, "ok");
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
    bool confirm = await DisplayAlert("delete folder",
        $"delete \"{folder.Name}\" and all notes inside?", "delete", "cancel");
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
    if (log == null) { await DisplayAlert("logs", "log service not initialized", "ok"); return; }
    var text = log.GetLogsText();
    if (string.IsNullOrEmpty(text)) { await DisplayAlert("logs", "no log entries yet", "ok"); return; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    using var stream = new MemoryStream(bytes);
    var fileName = $"notes_debug_{DateTime.Now:yyyyMMddHHmmss}.log";
    var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
    if (!result.IsSuccessful)
      await DisplayAlert("error", result.Exception?.Message ?? "save failed", "ok");
  }

  private async Task ImportBackupAsync()
  {
    bool confirmImport = await DisplayAlert("confirmation",
        "import will replace all existing data. continue?", "yes", "no");

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
        PickerTitle = "select backup file"
      });

      if (fileResult == null)
        return;

      await DisplayAlert("information", "starting import process...", "ok");

      string tempPath = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(fileResult.FullPath));
      using (var sourceStream = await fileResult.OpenReadAsync())
      using (var destStream = File.Create(tempPath))
      {
        await sourceStream.CopyToAsync(destStream);
      }

      await _exportService.ImportBackupAsync(tempPath);

      await DisplayAlert("success", "backup imported successfully. the app data has been replaced", "ok");

      await LoadFoldersAsync();
    }
    catch (Exception ex)
    {
      await DisplayAlert("error", $"failed to import backup: {ex.Message}", "ok");
    }
  }
}