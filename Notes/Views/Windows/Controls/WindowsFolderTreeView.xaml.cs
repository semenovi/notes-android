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

  public event EventHandler<string>? NoteMovedToFolder;

  private readonly ReactiveSyncService _reactiveSync;
  private readonly NoteManager _noteManager;
  private readonly Services.ProgressNotificationService _progressService;
  private readonly Services.ToastService _toastService;
  private Folder? _selectedFolder;
  private CancellationTokenSource? _loadCts;
  private List<Folder> _allFolders = new();

  private readonly FolderViewModel _topLevelVm =
      new(new Folder { Id = "", Name = "top level", Icon = MaterialIconCodes.FolderOpen });

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
    _allFolders = folders;
    ApplyFolders(folders);
  }

  private void ApplyFolders(List<Folder> folders)
  {
    var byId = Folders.ToDictionary(vm => vm.Folder.Id);
    var desired = BuildVisibleTree(folders, byId);
    desired.Insert(0, _topLevelVm);
    CollectionMerge.MergeInto(Folders, desired);

    var selected = _selectedFolder == null
        ? null
        : Folders.FirstOrDefault(f => f.Folder.Id == _selectedFolder.Id);
    if (selected != null)
    {
      foreach (var f in Folders) f.IsSelected = f == selected;
      _selectedFolder = selected.Folder;

    }
    else if (Folders.Count > 0)
    {

      SelectFolder(Folders[0]);
    }
    else
    {
      _selectedFolder = null;
    }
  }

  private static List<FolderViewModel> BuildVisibleTree(List<Folder> folders, Dictionary<string, FolderViewModel> byId)
  {
    var childrenByParent = folders
        .GroupBy(f => f.ParentId ?? string.Empty)
        .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name, NaturalSortComparer.Instance).ToList());

    var result = new List<FolderViewModel>();

    void Walk(string parentKey, int depth)
    {
      if (!childrenByParent.TryGetValue(parentKey, out var children)) return;
      foreach (var folder in children)
      {
        if (!byId.TryGetValue(folder.Id, out var vm))
          vm = new FolderViewModel(folder);
        else
          vm.Update(folder);

        vm.Depth = depth;
        vm.HasChildren = childrenByParent.ContainsKey(folder.Id);
        result.Add(vm);

        if (vm.HasChildren && vm.IsExpanded)
          Walk(folder.Id, depth + 1);
      }
    }

    Walk(string.Empty, 0);
    return result;
  }

  private void OnDisclosureTapped(object sender, EventArgs e)
  {
    if (sender is not Label label || label.BindingContext is not FolderViewModel vm) return;
    if (!vm.HasChildren) return;
    vm.IsExpanded = !vm.IsExpanded;
    ApplyFolders(_allFolders);
  }

  public async Task SyncIfEnabledAsync()
  {

    var settings = await _syncSettingsService.LoadAsync();

    if (settings.Enabled && _allFolders.Count == 0)
    {
      await RunSyncAsync();
      await LoadFoldersAsync();
    }
    if (_allFolders.Count == 0)
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

  private static bool IsTopLevelRow(FolderViewModel vm) => vm.Folder.Id.Length == 0;

  private static readonly Color FolderHoverColor = Color.FromArgb("#E8F0FE");

  private static Border? DropRowOf(object sender) => (sender as Element)?.Parent as Border;

  private void OnFolderRowDragOver(object sender, DragEventArgs e)
  {
    if (DropRowOf(sender) is Border border)
      border.BackgroundColor = FolderHoverColor;
  }

  private void OnFolderRowDragLeave(object sender, DragEventArgs e)
  {
    if (DropRowOf(sender) is Border border && border.BindingContext is FolderViewModel vm)
      border.BackgroundColor = vm.IsSelected ? Color.FromArgb("#D4D4D4") : Colors.Transparent;
  }

  private async void OnNoteDroppedOnFolder(object sender, DropEventArgs e)
  {
    var border = DropRowOf(sender);
    if (border == null) return;
    if (border.BindingContext is not FolderViewModel vm)
    {
      border.BackgroundColor = Colors.Transparent;
      return;
    }
    border.BackgroundColor = vm.IsSelected ? Color.FromArgb("#D4D4D4") : Colors.Transparent;

    if (!e.Data.Properties.TryGetValue("noteId", out var noteIdObj) || noteIdObj is not string noteId)
      return;

    var note = await _noteManager.GetNoteAsync(noteId);
    if (note == null || note.FolderId == vm.Folder.Id) return;

    note.FolderId = vm.Folder.Id;
    await _noteManager.UpdateNoteAsync(note);
    NoteMovedToFolder?.Invoke(this, noteId);
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
    var settings = await _syncSettingsService.LoadAsync();
    if (!settings.Enabled)
      _toastService.Show("please enable sync first");
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

  public async Task ShowOverallInfoAsync()
  {
    var page = Application.Current!.Windows[0].Page!;
    var folders = await _folderManager.GetAllFoldersAsync();
    var notes = await _noteManager.GetAllNotesAsync();
    await page.DisplayAlert("notes info", ItemInfoHelper.BuildOverallInfo(folders, notes), "ok");
  }

  private async void OnChangeFolderIconContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm) return;
    if (IsTopLevelRow(vm)) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var icon = await IconSet.PickAsync(page);
    if (icon == null) return;

    vm.Folder.Icon = icon;
    vm.Folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(vm.Folder);
    vm.Update(vm.Folder);
  }

  private async void OnNewSubfolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm) return;
    if (IsTopLevelRow(vm)) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var name = await page.DisplayPromptAsync("new folder", "folder name:");
    if (string.IsNullOrWhiteSpace(name)) return;

    await _folderManager.CreateFolderAsync(name, vm.Folder.Id);
    vm.IsExpanded = true;
    await LoadFoldersAsync();
  }

  private async void OnRenameFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    if (IsTopLevelRow(vm)) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newName = await page.DisplayPromptAsync("rename folder", "new name:", initialValue: vm.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == vm.Name) return;

    vm.Folder.Name = newName;
    vm.Folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(vm.Folder);
    vm.Update(vm.Folder);

    ApplyFolders(_allFolders);
    if (vm.IsSelected)
      FolderSelected?.Invoke(this, vm.Folder);
  }

  private async void OnFolderInfoContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    if (IsTopLevelRow(vm)) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var notes = await _noteManager.GetNotesAsync(vm.Folder.Id);
    await page.DisplayAlert("folder info", ItemInfoHelper.BuildFolderInfo(vm.Folder, notes), "ok");
  }

  private async void OnDeleteFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    if (IsTopLevelRow(vm)) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var folderId = vm.Folder.Id;
    var descendants = await _folderManager.GetDescendantFoldersAsync(folderId);
    string warning = descendants.Count > 0
        ? $"delete \"{vm.Name}\", its {descendants.Count} {(descendants.Count == 1 ? "subfolder" : "subfolders")} and all notes inside?"
        : $"delete \"{vm.Name}\" and all notes inside?";
    bool confirm = await page.DisplayAlert("delete folder", warning, "delete", "cancel");
    if (!confirm) return;

    foreach (var id in descendants.Select(f => f.Id).Append(folderId))
    {
      var notes = await _noteManager.GetNotesAsync(id);
      foreach (var note in notes)
        await _noteManager.DeleteNoteAsync(note.Id);
    }
    foreach (var descendant in descendants)
      await _folderManager.DeleteFolderAsync(descendant.Id);

    await _folderManager.DeleteFolderAsync(folderId);
    if (_selectedFolder?.Id == folderId || descendants.Any(d => d.Id == _selectedFolder?.Id))
      _selectedFolder = null;
    await LoadFoldersAsync();
  }

  private async Task ShowSyncSettingsDialogAsync(Page page)
  {
    var settings = await _syncSettingsService.LoadAsync();

    string? url = await page.DisplayPromptAsync("sync settings", "server url:",
        initialValue: settings.ServerUrl, placeholder: "http://notes.local:8080");
    if (url == null) return;

    string? token = await page.DisplayPromptAsync("sync settings",
        "api token (from /api/sync/setup on the server):",
        initialValue: settings.ApiToken, placeholder: "paste token here");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    _toastService.Show("settings saved");

    await _reactiveSync.RestartAsync();
    await LoadFoldersAsync();
  }

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
      _toastService.Show($"sync error: {ex.Message}");
    }
    catch (Exception ex)
    {
      _toastService.Show($"sync error: {ex.GetType().Name}: {ex.Message}");
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
      _toastService.Show("archive exported successfully");
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
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

      string tempPath = Path.Combine(AppPaths.CacheRoot,
          Path.GetFileName(fileResult.FullPath));

      using (var src = await fileResult.OpenReadAsync())
      using (var dst = File.Create(tempPath))
        await src.CopyToAsync(dst);

      await _exportService.ImportBackupAsync(tempPath);
      await LoadFoldersAsync();
      _toastService.Show("archive imported successfully");
    }
    catch (Exception ex)
    {
      _toastService.Show($"failed to import: {ex.Message}");
    }
  }
}

public class FolderViewModel : INotifyPropertyChanged
{
  public Folder Folder { get; private set; }
  public string Name => Folder.Name;
  public string Icon => Folder.Icon;

  public void Update(Folder folder)
  {
    Folder = folder;
    OnPropertyChanged(nameof(Name));
    OnPropertyChanged(nameof(Icon));
  }

  private int _depth;
  public int Depth
  {
    get => _depth;
    set
    {
      if (_depth != value)
      {
        _depth = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IndentWidth));
      }
    }
  }

  public double IndentWidth => Depth * 16;

  private bool _hasChildren;
  public bool HasChildren
  {
    get => _hasChildren;
    set
    {
      if (_hasChildren != value)
      {
        _hasChildren = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(DisclosureGlyph));
      }
    }
  }

  private bool _isExpanded = true;
  public bool IsExpanded
  {
    get => _isExpanded;
    set
    {
      if (_isExpanded != value)
      {
        _isExpanded = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(DisclosureGlyph));
      }
    }
  }

  public string DisclosureGlyph
  {
    get
    {
      if (!HasChildren) return "";
      return IsExpanded ? "▾" : "▸";
    }
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
