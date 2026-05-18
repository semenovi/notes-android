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
      FolderSelected?.Invoke(this, toSelect.Folder);
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
      await _folderManager.CreateFolderAsync("Общие");
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
    var name = await page.DisplayPromptAsync("Новая папка", "Название папки:");
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
    string syncToggleLabel = settings.Enabled ? "Синхронизация: ВКЛ" : "Синхронизация: ВЫКЛ";

    var action = await page.DisplayActionSheet(
        "Действия", "Отмена", null,
        syncToggleLabel,
        "Синхронизировать сейчас",
        "Настройки синхронизации...",
        "Экспорт архива",
        "Импорт архива");

    switch (action)
    {
      case "Синхронизация: ВКЛ":
      case "Синхронизация: ВЫКЛ":
        settings.Enabled = !settings.Enabled;
        await _syncSettingsService.SaveAsync(settings);
        if (settings.Enabled && string.IsNullOrEmpty(settings.ServerUrl))
          await ShowSyncSettingsDialogAsync(page);
        break;

      case "Синхронизировать сейчас":
        if (!settings.Enabled)
          await page.DisplayAlert("Синхронизация", "Сначала включите синхронизацию.", "OK");
        else
        {
          await RunSyncAsync();
          await LoadFoldersAsync();
        }
        break;

      case "Настройки синхронизации...":
        await ShowSyncSettingsDialogAsync(page);
        break;

      case "Экспорт архива":
        await ExportAsync(page);
        break;

      case "Импорт архива":
        await ImportAsync(page);
        break;
    }
  }

  private async void OnRenameFolderContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not FolderViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newName = await page.DisplayPromptAsync("Переименовать папку", "Новое название:", initialValue: vm.Name);
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

    bool confirm = await page.DisplayAlert("Удалить папку",
        $"Удалить «{vm.Name}» и все заметки в ней?", "Удалить", "Отмена");
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

    string? url = await page.DisplayPromptAsync("Настройки синхронизации", "URL сервера:",
        initialValue: settings.ServerUrl, placeholder: "http://46.148.142.210:8080");
    if (url == null) return;

    string? token = await page.DisplayPromptAsync("Настройки синхронизации",
        "API токен (из /api/sync/setup на сервере):",
        initialValue: settings.ApiToken, placeholder: "вставьте токен");
    if (token == null) return;

    settings.ServerUrl = url.TrimEnd('/');
    settings.ApiToken = token.Trim();
    settings.Enabled = true;

    await _syncSettingsService.SaveAsync(settings);
    await page.DisplayAlert("Настройки синхронизации", "Настройки сохранены.", "OK");

    await _reactiveSync.RestartAsync();
    await RunSyncAsync();
    await LoadFoldersAsync();
  }

  private async Task RunSyncAsync()
  {
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
        await page.DisplayAlert("Ошибка синхронизации", ex.Message, "OK");
    }
    catch { /* transient network error — periodic sync will retry */ }
  }

  private async Task ExportAsync(Page page)
  {
    try
    {
      await _exportService.ExportBackupAsync();
      await page.DisplayAlert("Готово", "Архив успешно экспортирован.", "OK");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("Ошибка", ex.Message, "OK");
    }
  }

  private async Task ImportAsync(Page page)
  {
    bool confirm = await page.DisplayAlert("Подтверждение",
        "Импорт заменит все существующие данные. Продолжить?", "Да", "Отмена");
    if (!confirm) return;

    try
    {
      var fileResult = await FilePicker.PickAsync(new PickOptions
      {
        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
          { DevicePlatform.WinUI, new[] { ".zip" } }
        }),
        PickerTitle = "Выберите файл резервной копии"
      });

      if (fileResult == null) return;

      string tempPath = Path.Combine(FileSystem.CacheDirectory,
          Path.GetFileName(fileResult.FullPath));

      using (var src = await fileResult.OpenReadAsync())
      using (var dst = File.Create(tempPath))
        await src.CopyToAsync(dst);

      await _exportService.ImportBackupAsync(tempPath);
      await LoadFoldersAsync();
      await page.DisplayAlert("Готово", "Архив успешно импортирован.", "OK");
    }
    catch (Exception ex)
    {
      await page.DisplayAlert("Ошибка", $"Не удалось импортировать: {ex.Message}", "OK");
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
    set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
  }

  public FolderViewModel(Folder folder) => Folder = folder;

  public event PropertyChangedEventHandler? PropertyChanged;
  protected void OnPropertyChanged([CallerMemberName] string name = "") =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
