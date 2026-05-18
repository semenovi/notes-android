using Notes.Models;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Windows.Controls;

public partial class WindowsNoteListView : ContentView
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private string? _currentFolderId;
  private List<NoteViewModel> _allNotes = new();

  public ObservableCollection<NoteViewModel> Notes { get; } = new();

  public event EventHandler<Note>? NoteSelected;
  public event EventHandler<Note>? NoteDeleted;

  public WindowsNoteListView()
  {
    InitializeComponent();
    var services = App.Current!.Handler!.MauiContext!.Services;
    _noteManager = services.GetService<NoteManager>()!;
    _mediaManager = services.GetService<MediaManager>()!;
    NotesCollectionView.ItemsSource = Notes;
    services.GetService<ReactiveSyncService>()!.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied()
  {
    if (!string.IsNullOrEmpty(_currentFolderId))
      await LoadNotesAsync(_currentFolderId);
  }

  public void SetFolderName(string name)
  {
    FolderTitleLabel.Text = name;
  }

  public async Task LoadNotesAsync(string folderId)
  {
    _currentFolderId = folderId;

    var notes = await _noteManager.GetNotesAsync(folderId);
    var sorted = notes.OrderByDescending(n => n.Modified).ToList();

    var viewModels = new List<NoteViewModel>(sorted.Count);
    foreach (var note in sorted)
    {
      var images = await ResolvePreviewImagesAsync(note.Content);
      viewModels.Add(new NoteViewModel(note, images));
    }

    _allNotes = viewModels;
    ApplySearch(SearchEntry.Text);
  }

  private async Task<IReadOnlyList<ImageSource>> ResolvePreviewImagesAsync(string? content)
  {
    if (string.IsNullOrEmpty(content)) return Array.Empty<ImageSource>();

    var result = new List<ImageSource>();
    var matches = System.Text.RegularExpressions.Regex.Matches(
        content, @"!\[[^\]]*\]\(media:([^)]+)\)");

    foreach (System.Text.RegularExpressions.Match m in matches.Cast<System.Text.RegularExpressions.Match>().Take(4))
    {
      try
      {
        var item = await _mediaManager.GetMediaAsync(m.Groups[1].Value);
        if (item != null)
        {
          var absPath = Path.Combine(FileSystem.AppDataDirectory, "Notes", item.StoragePath);
          if (File.Exists(absPath))
            result.Add(ImageSource.FromFile(absPath));
        }
      }
      catch { /* skip unresolvable images */ }
    }

    return result;
  }

  private void ApplySearch(string? query)
  {
    Notes.Clear();
    var filtered = string.IsNullOrWhiteSpace(query)
        ? _allNotes
        : _allNotes.Where(n =>
            n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            n.Preview.Contains(query, StringComparison.OrdinalIgnoreCase));

    foreach (var note in filtered)
      Notes.Add(note);
  }

  private void OnSearchTextChanged(object sender, TextChangedEventArgs e) =>
      ApplySearch(e.NewTextValue);

  private void OnNoteTapped(object sender, EventArgs e)
  {
    if (sender is Grid grid && grid.BindingContext is NoteViewModel vm)
      SelectNote(vm);
  }

  private void SelectNote(NoteViewModel vm)
  {
    foreach (var n in Notes) n.IsSelected = false;
    vm.IsSelected = true;
    NoteSelected?.Invoke(this, vm.Note);
  }

  private async void OnNewNoteButtonClicked(object sender, EventArgs e)
  {
    if (string.IsNullOrEmpty(_currentFolderId))
    {
      await Application.Current!.Windows[0].Page!.DisplayAlert(
          "Внимание", "Выберите папку", "OK");
      return;
    }

    var page = Application.Current!.Windows[0].Page!;
    var title = await page.DisplayPromptAsync("Новая заметка", "Название:");
    if (!string.IsNullOrWhiteSpace(title))
    {
      var note = await _noteManager.CreateNoteAsync(title, _currentFolderId);
      await LoadNotesAsync(_currentFolderId);

      var created = Notes.FirstOrDefault(n => n.Note.Id == note.Id);
      if (created != null) SelectNote(created);
    }
  }

  private async void OnRenameNoteContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newTitle = await page.DisplayPromptAsync("Переименовать заметку", "Новое название:", initialValue: vm.Title);
    if (string.IsNullOrWhiteSpace(newTitle) || newTitle == vm.Title) return;

    vm.Note.Title = newTitle;
    await _noteManager.UpdateNoteAsync(vm.Note);

    var idx = Notes.IndexOf(vm);
    var allIdx = _allNotes.IndexOf(vm);
    var isSelected = vm.IsSelected;
    var updated = new NoteViewModel(vm.Note, vm.PreviewImages) { IsSelected = isSelected };

    if (idx >= 0) Notes[idx] = updated;
    if (allIdx >= 0) _allNotes[allIdx] = updated;

    if (isSelected)
      NoteSelected?.Invoke(this, vm.Note);
  }

  private async void OnDeleteNoteContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    bool confirm = await page.DisplayAlert("Удалить заметку",
        $"Удалить «{vm.Title}»?", "Удалить", "Отмена");
    if (!confirm) return;

    await _noteManager.DeleteNoteAsync(vm.Note.Id);
    RemoveNote(vm.Note.Id);
    NoteDeleted?.Invoke(this, vm.Note);
  }

  public void RemoveNote(string noteId)
  {
    var vm = Notes.FirstOrDefault(n => n.Note.Id == noteId);
    if (vm != null)
    {
      Notes.Remove(vm);
      _allNotes.RemoveAll(n => n.Note.Id == noteId);
    }
  }

  public void RefreshNote(Note updatedNote)
  {
    var existing = Notes.FirstOrDefault(n => n.Note.Id == updatedNote.Id);
    if (existing != null)
    {
      var index = Notes.IndexOf(existing);
      Notes[index] = new NoteViewModel(updatedNote, existing.PreviewImages)
      {
        IsSelected = existing.IsSelected
      };
    }
  }
}

public class NoteViewModel : INotifyPropertyChanged
{
  public Note Note { get; }
  public string Title => Note.Title;
  public string Preview => GetPreview();
  public string ModifiedString => Note.Modified.ToString("dd.MM HH:mm");
  public IReadOnlyList<ImageSource> PreviewImages { get; }

  private bool _isSelected;
  public bool IsSelected
  {
    get => _isSelected;
    set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
  }

  public NoteViewModel(Note note, IReadOnlyList<ImageSource>? images = null)
  {
    Note = note;
    PreviewImages = images ?? Array.Empty<ImageSource>();
  }

  private string GetPreview()
  {
    if (string.IsNullOrEmpty(Note.Content)) return "Нет текста";

    var text = Note.Content;

    // strip fenced code blocks first
    text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", " ");
    // strip images
    text = System.Text.RegularExpressions.Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
    // strip links — keep label
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
    // strip headers
    text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
    // strip bold/italic
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\*{1,3}([^*]*)\*{1,3}", "$1");
    text = System.Text.RegularExpressions.Regex.Replace(text, @"_{1,3}([^_]*)_{1,3}", "$1");
    // strip inline code
    text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
    // strip blockquotes and list markers
    text = System.Text.RegularExpressions.Regex.Replace(text, @"^[>*+\-]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
    text = System.Text.RegularExpressions.Regex.Replace(text, @"^\d+\.\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);

    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    return text.Length > 80 ? text[..80] + "…" : text;
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  protected void OnPropertyChanged([CallerMemberName] string name = "") =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
