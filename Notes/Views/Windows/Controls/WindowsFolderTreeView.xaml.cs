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
  private Folder? _selectedFolder;

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
    FoldersCollectionView.ItemsSource = Folders;
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied()
  {
    var selectedId = _selectedFolder?.Id;
    Folders.Clear();
    var folders = await _folderManager.GetAllFoldersAsync();
    foreach (var folder in folders)
      Folders.Add(new FolderViewModel(folder));

    var toSelect = Folders.FirstOrDefault(f => f.Folder.Id == selectedId) ?? Folders.FirstOrDefault();
    if (toSelect != null)
    {
      toSelect.IsSelected = true;
      _selectedFolder = toSelect.Folder;
      // Don't fire FolderSelected — WindowsNoteListView handles its own refresh via RemoteChangesApplied.
    }
  }

  public async Task LoadFoldersAsync()
  {
    Folders.Clear();
    var folders = await _folderManager.GetAllFoldersAsync();
    foreach (var folder in folders)
      Folders.Add(new FolderViewModel(folder));

    if (Folders.Count > 0)
    {
      Folders[0].IsSelected = true;
      FolderSelected?.Invoke(this, Folders[0].Folder);
    }
  }

  public async Task SyncIfEnabledAsync()
  {
    var settings = await _syncSettingsService.LoadAsync();
    if (settings.Enabled)
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
    var name = await page.DisplayPromptAsync("New Folder", "Folder name:");
    if (!string.IsNullOrWhiteSpace(name))
    {
      await _folderManager.CreateFolderAsync(name);
      await LoadFoldersAsync();
    }
  }

  private async void OnMoreButtonClicked(object sender, EventArgs e)
  {
    var page = Application.Current!.Windows[0].Page!;
    var settings = await _syncSettingsService.LoadAsync();
    string syncToggleLabel = settings.Enabled ? "Sync: ON" : "Sync: OFF";

    var action = await page.DisplayActionSheet(
        "Actions", "Cancel", null,
        syncToggleLabel,
        "Sync Now",
        "Sync Settings...",
        "Export Archive",
        "Import Archive");

    switch (action)
    {
      case "Sync: ON":
      case "Sync: OFF":
        settings.Enabled = !settings.Enabled;
        await _syncSettingsService.SaveAsync(settings);
        if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
          await ShowSyncSettingsDialogAsync(page);
        break;

      case "Sync Now":
        if (!settings.Enabled)
          await page.DisplayAlert("Sync", "Please enable sync first.", "OK");
        else
        {
          await RunSyncAsync();
          await LoadFoldersAsync();
        }
        break;

      case "Sync Settings...":
        await ShowSyncSettingsDialogAsync(page);
        break;

      case "Export Archive":
        await ExportAsync(page);
        break;

      case "Import Archive":
        await ImportAsync(page);
        break;
    }
  }

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

    var idx = Folders.IndexOf(vm);
    var isSelected = vm.IsSelected;
    if (idx >= 0)
      Folders[idx] = new FolderViewModel(vm.Folder) { IsSelected = isSelected };
  }

  private async void OnRenameFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newName = await page.DisplayPromptAsync("Rename Folder", "New name:", initialValue: vm.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

    vm.Folder.Name = newName;
    vm.Folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(vm.Folder);

    var idx = Folders.IndexOf(vm);
    var isSelected = vm.IsSelected;
    if (idx >= 0)
    {
      Folders[idx] = new FolderViewModel(vm.Folder) { IsSelected = isSelected };
      if (isSelected)
        FolderSelected?.Invoke(this, vm.Folder);
    }
  }

  private async void OnDeleteFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    bool confirm = await page.DisplayAlert("Delete Folder",
        $"Delete \"{vm.Name}\" and all notes inside?", "Delete", "Cancel");
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

    string? url = await page.DisplayPromptAsync("Sync Settings", "Server URL:",
        initialValue: settings.ServerUrl, placeholder: "http://46.148.142.210:8080");
    if (url == null) return;

    string? token = await page.DisplayPromptAsync("Sync Settings",
        "API token (from /api/sync/setup on the server):",
        initialValue: settings.ApiToken, placeholder: "paste token here");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    await page.DisplayAlert("Sync Settings", "Settings saved.", "OK");

    await _reactiveSync.RestartAsync();
    await RunSyncAsync();
    await LoadFoldersAsync();
  }

  private async Task RunSyncAsync()
  {
    using var session = _progressService.Begin("Syncing");
    try
    {
      await _syncManager.SynchronizeAsync(new SyncProfile
      {
        Name = "Network",
        Protocol = SyncProtocolType.Network,
      });
    }
    catch (InvalidOperationException ex)
    {
      var page = Application.Current?.Windows.FirstOrDefault()?.Page;
      if (page != null)
        await page.DisplayAlert("Sync Error", ex.Message, "OK");
    }
    catch (Exception ex)
    {
      var page = Application.Current?.Windows.FirstOrDefault()?.Page;
      if (page != null)
        await page.DisplayAlert("Sync Error", ex.GetType().Name + ": " + ex.Message, "OK");
    }
  }

  private async Task ExportAsync(Page page)
  {
    try
    {
      await _exportService.ExportBackupAsync();
      await page.DisplayAlert("Done", "Archive exported successfully.", "OK");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("Error", ex.Message, "OK");
    }
  }

  private async Task ImportAsync(Page page)
  {
    bool confirm = await page.DisplayAlert("Confirmation",
        "Import will replace all existing data. Continue?", "Yes", "Cancel");
    if (!confirm) return;

    try
    {
      var fileResult = await FilePicker.PickAsync(new PickOptions
      {
        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
          { DevicePlatform.WinUI, new[] { ".zip" } }
        }),
        PickerTitle = "Select backup file"
      });

      if (fileResult == null) return;

      string tempPath = Path.Combine(FileSystem.CacheDirectory,
          Path.GetFileName(fileResult.FullPath));

      using (var src = await fileResult.OpenReadAsync())
      using (var dst = File.Create(tempPath))
        await src.CopyToAsync(dst);

      await _exportService.ImportBackupAsync(tempPath);
      await LoadFoldersAsync();
      await page.DisplayAlert("Done", "Archive imported successfully.", "OK");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("Error", $"Failed to import: {ex.Message}", "OK");
    }
  }
}

public class FolderViewModel : INotifyPropertyChanged
{
  public Folder Folder { get; }
  public string Name => Folder.Name;
  public string Icon => Folder.Icon;

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
