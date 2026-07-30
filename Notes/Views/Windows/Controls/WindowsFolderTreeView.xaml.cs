using Notes.Helpers;
using Notes.Models;
using Notes.Services.Export;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Windows.Controls;

public partial class WindowsFolderTreeView : ContentView
{
  private readonly FolderManager _folderManager;
  private readonly ExportService _exportService;
  private readonly SyncManager _syncManager;
  private readonly SyncSettingsService _syncSettingsService;
  public ObservableCollection<FolderViewModel> Folders { get; } = new();

  public event EventHandler<Folder>? FolderSelected;

  private readonly ReactiveSyncService _reactiveSync;
  private readonly NoteManager _noteManager;
  private readonly Services.ProgressNotificationService _progressService;
  private readonly Services.ToastService _toastService;
  private Folder? _selectedFolder;
  private CancellationTokenSource? _loadCts;

  public WindowsFolderTreeView()
  {
    InitializeComponent();
    var services = App.Current!.Handler!.MauiContext!.Services;
    _folderManager = services.GetService<FolderManager>()!;
    _noteManager = services.GetService<NoteManager>()!;
    _exportService = services.GetService<ExportService>()!;
    _syncManager = services.GetService<SyncManager>()!;
    _syncSettingsService = services.GetService<SyncSettingsService>()!;
    _reactiveSync = services.GetService<ReactiveSyncService>()!;
    _progressService = services.GetService<Services.ProgressNotificationService>()!;
    _toastService = services.GetService<Services.ToastService>()!;
    FoldersCollectionView.ItemsSource = Folders;
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied()
  {
    await LoadFoldersAsync();
  }

  public async Task LoadFoldersAsync()
  {
    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var folders = await _folderManager.GetAllFoldersAsync();
    if (cts.IsCancellationRequested) return;
    ApplyFolders(folders);
  }

  // Diff-merge into the existing collection: unchanged folders keep their view
  // models (no rebuild, no flicker) and the current selection survives a refresh.
  private void ApplyFolders(List<Folder> folders)
  {
    var byId = Folders.ToDictionary(vm => vm.Folder.Id);
    var desired = new List<FolderViewModel>(folders.Count);
    foreach (var folder in folders.OrderBy(f => f.Name, NaturalSortComparer.Instance))
    {
      if (byId.TryGetValue(folder.Id, out var vm))
        vm.Update(folder);
      else
        vm = new FolderViewModel(folder);
      desired.Add(vm);
    }
    CollectionMerge.MergeInto(Folders, desired);

    var selected = _selectedFolder == null
        ? null
        : Folders.FirstOrDefault(f => f.Folder.Id == _selectedFolder.Id);
    if (selected != null)
    {
      foreach (var f in Folders) f.IsSelected = f == selected;
      _selectedFolder = selected.Folder;
      // Selection unchanged — don't fire FolderSelected, the note list refreshes itself.
    }
    else if (Folders.Count > 0)
    {
      // Nothing selected yet, or the selected folder was deleted — fall back to
      // the first folder and notify so the note list follows.
      SelectFolder(Folders[0]);
    }
    else
    {
      _selectedFolder = null;
    }
  }

  public async Task SyncIfEnabledAsync()
  {
    // Only block on a full sync when there is no local data yet (fresh install).
    // Otherwise ReactiveSyncService already runs an initial sync in the background
    // and fires RemoteChangesApplied when something new arrives.
    var settings = await _syncSettingsService.LoadAsync();
    if (settings.Enabled && Folders.Count == 0)
    {
      await RunSyncAsync();
      await LoadFoldersAsync();
    }
    if (Folders.Count == 0)
    {
      await _folderManager.CreateFolderAsync("General");
      await LoadFoldersAsync();
    }
  }

  private void OnFolderTapped(object sender, EventArgs e)
  {
    if (sender is Grid grid && grid.BindingContext is FolderViewModel vm)
      SelectFolder(vm);
  }

  private void SelectFolder(FolderViewModel vm)
  {
    foreach (var f in Folders) f.IsSelected = false;
    vm.IsSelected = true;
    _selectedFolder = vm.Folder;
    FolderSelected?.Invoke(this, vm.Folder);
  }

  private async void OnNewFolderButtonClicked(object sender, EventArgs e)
  {
    var page = Application.Current!.Windows[0].Page!;
    var name = await page.DisplayPromptAsync("new folder", "folder name:");
    if (!string.IsNullOrWhiteSpace(name))
    {
      await _folderManager.CreateFolderAsync(name);
      await LoadFoldersAsync();
    }
  }

  public event EventHandler? MenuRequested;

  private void OnMenuButtonClicked(object sender, EventArgs e)
    => MenuRequested?.Invoke(this, EventArgs.Empty);

  public async Task ToggleSyncAsync()
  {
    var page = Application.Current!.Windows[0].Page!;
    var settings = await _syncSettingsService.LoadAsync();
    settings.Enabled = !settings.Enabled;
    await _syncSettingsService.SaveAsync(settings);
    if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
      await ShowSyncSettingsDialogAsync(page);
  }

  public async Task SyncNowAsync()
  {
    var page = Application.Current!.Windows[0].Page!;
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled)
      await page.DisplayAlert("sync", "please enable sync first", "ok");
    else
    {
      int applied = await RunSyncAsync();
      await LoadFoldersAsync();
      if (applied >= 0)
        _toastService.Show(applied > 0
            ? $"sync complete: {applied} {(applied == 1 ? "change" : "changes")} applied"
            : "sync complete, no changes");
    }
  }

  public async Task ShowSyncSettingsAsync()
    => await ShowSyncSettingsDialogAsync(Application.Current!.Windows[0].Page!);

  public async Task ExportArchiveAsync()
    => await ExportAsync(Application.Current!.Windows[0].Page!);

  public async Task ImportArchiveAsync()
    => await ImportAsync(Application.Current!.Windows[0].Page!);

  private async void OnChangeFolderIconContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var icon = await IconSet.PickAsync(page);
    if (icon == null) return;

    vm.Folder.Icon = icon;
    vm.Folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(vm.Folder);
    vm.Update(vm.Folder);
  }

  private async void OnRenameFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newName = await page.DisplayPromptAsync("rename folder", "new name:", initialValue: vm.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

    vm.Folder.Name = newName;
    vm.Folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(vm.Folder);
    vm.Update(vm.Folder);
    ApplyFolders(Folders.Select(f => f.Folder).ToList());
    if (vm.IsSelected)
      FolderSelected?.Invoke(this, vm.Folder);
  }

  private async void OnDeleteFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    bool confirm = await page.DisplayAlert("delete folder",
        $"delete \"{vm.Name}\" and all notes inside?", "delete", "cancel");
    if (!confirm) return;

    var folderId = vm.Folder.Id;
    var notes = await _noteManager.GetNotesAsync(folderId);
    foreach (var note in notes)
      await _noteManager.DeleteNoteAsync(note.Id);

    await _folderManager.DeleteFolderAsync(folderId);
    if (_selectedFolder?.Id == folderId) _selectedFolder = null;
    await LoadFoldersAsync();
  }

  private async Task ShowSyncSettingsDialogAsync(Page page)
  {
    var settings = await _syncSettingsService.LoadAsync();

    string? url = await page.DisplayPromptAsync("sync settings", "server url:",
        initialValue: settings.ServerUrl, placeholder: "http://46.148.142.210:8080");
    if (url == null) return;

    string? token = await page.DisplayPromptAsync("sync settings",
        "api token (from /api/sync/setup on the server):",
        initialValue: settings.ApiToken, placeholder: "paste token here");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    await page.DisplayAlert("sync settings", "settings saved", "ok");

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
      return await _syncManager.SynchronizeAsync(new SyncProfile
      {
        Name = "Network",
        Protocol = SyncProtocolType.Network,
      }, session.Report);
    }
    catch (InvalidOperationException ex)
    {
      var page = Application.Current?.Windows.FirstOrDefault()?.Page;
      if (page != null)
        await page.DisplayAlert("sync error", ex.Message, "ok");
    }
    catch (Exception ex)
    {
      var page = Application.Current?.Windows.FirstOrDefault()?.Page;
      if (page != null)
        await page.DisplayAlert("sync error", ex.GetType().Name + ": " + ex.Message, "ok");
    }
    return -1;
  }

  private async Task ExportAsync(Page page)
  {
#if WINDOWS
    string? targetPath = await PickExportPathAsync();
    if (targetPath == null) return;

    using var session = _progressService.Begin("exporting archive", delayMs: 0, priority: 1);
    try
    {
      await _exportService.ExportBackupToFileAsync(targetPath);
      _toastService.Show("archive exported successfully");
    }
    catch (Exception ex)
    {
      _toastService.Show($"export failed: {ex.Message}");
    }
#else
    try
    {
      await _exportService.ExportBackupAsync();
      await page.DisplayAlert("done", "archive exported successfully", "ok");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("error", ex.Message, "ok");
    }
#endif
  }

#if WINDOWS
  private static async Task<string?> PickExportPathAsync()
  {
    var picker = new global::Windows.Storage.Pickers.FileSavePicker
    {
      SuggestedFileName = DateTime.Now.ToString("yyyyMMddHHmmss"),
      DefaultFileExtension = ".zip",
    };
    picker.FileTypeChoices.Add("zip archive", new List<string> { ".zip" });

    var mauiWindow = Application.Current?.Windows.FirstOrDefault();
    if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win) return null;
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

    var file = await picker.PickSaveFileAsync();
    return file?.Path;
  }
#endif

  private async Task ImportAsync(Page page)
  {
    bool confirm = await page.DisplayAlert("confirmation",
        "import will replace all existing data. continue?", "yes", "cancel");
    if (!confirm) return;

    try
    {
      var fileResult = await FilePicker.PickAsync(new PickOptions
      {
        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
          { DevicePlatform.WinUI, new[] { ".zip" } }
        }),
        PickerTitle = "select backup file"
      });

      if (fileResult == null) return;

      string tempPath = Path.Combine(FileSystem.CacheDirectory,
          Path.GetFileName(fileResult.FullPath));

      using (var src = await fileResult.OpenReadAsync())
      using (var dst = File.Create(tempPath))
        await src.CopyToAsync(dst);

      await _exportService.ImportBackupAsync(tempPath);
      await LoadFoldersAsync();
      await page.DisplayAlert("done", "archive imported successfully", "ok");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("error", $"failed to import: {ex.Message}", "ok");
    }
  }
}

public class FolderViewModel : INotifyPropertyChanged
{
  public Folder Folder { get; private set; }
  public string Name => Folder.Name;
  public string Icon => Folder.Icon;

  // Swaps the underlying folder in place so the bound row refreshes
  // without being recreated (keeps the list stable during sync updates).
  public void Update(Folder folder)
  {
    Folder = folder;
    OnPropertyChanged(nameof(Name));
    OnPropertyChanged(nameof(Icon));
  }

  private bool _isSelected;
  public bool IsSelected
  {
    get => _isSelected;
    set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
  }

  public FolderViewModel(Folder folder) => Folder = folder;

  public event PropertyChangedEventHandler? PropertyChanged;
  protected void OnPropertyChanged([CallerMemberName] string name = "") =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
