using CommunityToolkit.Maui.Storage;
using Notes.Helpers;
using Notes.Models;
using Notes.Services;

namespace Notes.Views.Pages;

// Root-only behaviour: when NotesPage stands in for the top level it also carries the
// app-wide actions that used to live on the now-removed FoldersPage (sync, backup, info).
public partial class NotesPage
{
  private bool _rootChromeReady;
  private ToolbarItem? _syncToggleItem;

  private void EnsureRootChrome()
  {
    if (_rootChromeReady) return;
    _rootChromeReady = true;

    FolderName = "notes";
    EmptyStateTitle.Text = "no notes yet";
    EmptyStateSubtitle.Text = "add a note or folder";

    ToolbarItems.Clear();

    _syncToggleItem = new ToolbarItem { Text = "sync: off", Order = ToolbarItemOrder.Secondary };
    _syncToggleItem.Clicked += OnSyncToggleClicked;
    ToolbarItems.Add(_syncToggleItem);

    AddSecondaryItem("sync now", OnSyncNowClicked);
    AddSecondaryItem("sync settings...", OnSyncSettingsClicked);
    AddSecondaryItem("export backup", OnExportBackupClicked);
    AddSecondaryItem("import backup", OnImportBackupClicked);
    AddSecondaryItem("info", OnOverallInfoClicked);
    AddSecondaryItem("export logs", OnExportLogsClicked);

    _ = UpdateSyncToggleTextAsync();
  }

  private void AddSecondaryItem(string text, EventHandler handler)
  {
    var item = new ToolbarItem { Text = text, Order = ToolbarItemOrder.Secondary };
    item.Clicked += handler;
    ToolbarItems.Add(item);
  }

  private async Task UpdateSyncToggleTextAsync()
  {
    if (_syncToggleItem == null) return;
    var settings = await _syncSettingsService.LoadAsync();
    _syncToggleItem.Text = settings.Enabled ? "sync: on" : "sync: off";
  }

  private async void OnSyncToggleClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    settings.Enabled = !settings.Enabled;
    await _syncSettingsService.SaveAsync(settings);
    if (_syncToggleItem != null)
      _syncToggleItem.Text = settings.Enabled ? "sync: on" : "sync: off";

#if ANDROID
    if (settings.Enabled && !string.IsNullOrEmpty(settings.ServerUrl))
      SyncJobScheduler.EnsurePeriodic();
    else if (!settings.Enabled)
      SyncJobScheduler.CancelAll();
#endif

    if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
      await ShowSyncSettingsDialogAsync();
  }

  private async void OnSyncNowClicked(object sender, EventArgs e)
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled)
    {
      _toastService.Show("enable sync first");
      return;
    }
    int applied = await RunSyncAsync();
    await LoadItemsAsync();
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
    _toastService.Show("settings saved");

#if ANDROID
    SyncJobScheduler.EnsurePeriodic();
#endif

    // RestartAsync already runs an immediate sync in the background — a second
    // RunSyncAsync() here would race it (see the same note in the old FoldersPage).
    await _reactiveSync.RestartAsync();
    await LoadItemsAsync();
  }

  // Returns the number of remote changes applied, or -1 if the sync failed.
  private async Task<int> RunSyncAsync()
  {
    using var session = _progressService.Begin("syncing");
    try
    {
      return await Task.Run(() => _syncManager.SynchronizeAsync(new SyncProfile
      {
        Name = "Network",
        Protocol = SyncProtocolType.Network,
      }, session.Report));
    }
    catch (InvalidOperationException ex)
    {
      _toastService.Show($"sync error: {ex.Message}");
    }
    catch (Exception ex)
    {
      _toastService.Show($"sync error: {ex.GetType().Name}: {ex.Message}");
    }
    return -1;
  }

  private async void OnExportBackupClicked(object sender, EventArgs e)
  {
    try
    {
      await _exportService.ExportBackupAsync();
      _toastService.Show("backup exported successfully");
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
    }
  }

  private async void OnImportBackupClicked(object sender, EventArgs e)
  {
    await ImportBackupAsync();
  }

  private async void OnOverallInfoClicked(object sender, EventArgs e)
  {
    var folders = await _folderManager.GetAllFoldersAsync();
    var notes = await _noteManager.GetAllNotesAsync();
    await DisplayAlert("notes info", ItemInfoHelper.BuildOverallInfo(folders, notes), "ok");
  }

  private async void OnExportLogsClicked(object sender, EventArgs e)
  {
    var log = DebugLogService.Current;
    if (log == null) { _toastService.Show("log service not initialized"); return; }
    var text = log.GetLogsText();
    if (string.IsNullOrEmpty(text)) { _toastService.Show("no log entries yet"); return; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    using var stream = new MemoryStream(bytes);
    var fileName = $"notes_debug_{DateTime.Now:yyyyMMddHHmmss}.log";
    var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
    if (!result.IsSuccessful)
      _toastService.Show(result.Exception?.Message ?? "save failed");
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

      _toastService.Show("starting import process...");

      string tempPath = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(fileResult.FullPath));
      using (var sourceStream = await fileResult.OpenReadAsync())
      using (var destStream = File.Create(tempPath))
      {
        await sourceStream.CopyToAsync(destStream);
      }

      await _exportService.ImportBackupAsync(tempPath);

      _toastService.Show("backup imported successfully. the app data has been replaced");

      await LoadItemsAsync();
    }
    catch (Exception ex)
    {
      _toastService.Show($"failed to import backup: {ex.Message}");
    }
  }
}
