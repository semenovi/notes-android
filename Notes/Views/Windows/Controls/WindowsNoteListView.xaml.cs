using Notes.Helpers;
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
  private readonly Services.ToastService _toastService;
  private string? _currentFolderId;
  private string? _selectedNoteId;
  private List<NoteViewModel> _allNotes = new();
  private CancellationTokenSource? _loadCts;

  public ObservableCollection<NoteViewModel> Notes { get; } = new();

  public event EventHandler<Note>? NoteSelected;
  public event EventHandler<Note>? NoteDeleted;

  public WindowsNoteListView()
  {
    InitializeComponent();
    var services = App.Current!.Handler!.MauiContext!.Services;
    _noteManager = services.GetService<NoteManager>()!;
    _mediaManager = services.GetService<MediaManager>()!;
    _toastService = services.GetService<Services.ToastService>()!;
    NotesCollectionView.ItemsSource = Notes;
    services.GetService<ReactiveSyncService>()!.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied()
  {
    // LoadNotesAsync merges into the existing list and keeps the current
    // selection; NoteSelected is not re-fired, so the editor stays untouched.
    if (string.IsNullOrEmpty(_currentFolderId)) return;
    await LoadNotesAsync(_currentFolderId);
  }

  public void SetFolderName(string name)
  {
    FolderTitleLabel.Text = name;
  }

  public async Task LoadNotesAsync(string folderId)
  {
    if (_currentFolderId != folderId)
      _selectedNoteId = null;
    _currentFolderId = folderId;

    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;

    var notes = await _noteManager.GetNotesAsync(folderId);
    var sorted = notes.OrderBy(n => n.Title, NaturalSortComparer.Instance).ToList();

    // Reuse view models of unchanged notes (same id + Modified) so their rows —
    // including already-resolved preview images — stay as-is. Only new or
    // modified notes get a fresh view model, with images resolved in parallel.
    var existing = _allNotes.ToDictionary(vm => vm.Note.Id);
    var viewModels = new List<NoteViewModel?>(sorted.Count);
    var toResolve = new List<(int Index, Note Note)>();
    foreach (var note in sorted)
    {
      if (existing.TryGetValue(note.Id, out var vm) && vm.Note.Modified == note.Modified)
      {
        viewModels.Add(vm);
      }
      else
      {
        toResolve.Add((viewModels.Count, note));
        viewModels.Add(null);
      }
    }

    var resolved = await Task.WhenAll(toResolve.Select(t => ResolvePreviewImagesAsync(t.Note.Content)));
    if (cts.IsCancellationRequested) return;

    for (int i = 0; i < toResolve.Count; i++)
      viewModels[toResolve[i].Index] = new NoteViewModel(toResolve[i].Note, resolved[i]);

    _allNotes = viewModels.Cast<NoteViewModel>().ToList();

    if (_selectedNoteId != null)
    {
      var sel = _allNotes.FirstOrDefault(vm => vm.Note.Id == _selectedNoteId);
      foreach (var vm in _allNotes) vm.IsSelected = vm == sel;
      if (sel == null) _selectedNoteId = null;
    }

    ApplySearch(SearchEntry.Text);
  }

  private static readonly System.Text.RegularExpressions.Regex MediaRefRegex =
      new(@"!\[[^\]]*\]\(media:([^)]+)\)", System.Text.RegularExpressions.RegexOptions.Compiled);

  private async Task<IReadOnlyList<ImageSource>> ResolvePreviewImagesAsync(string? content)
  {
    if (string.IsNullOrEmpty(content)) return Array.Empty<ImageSource>();

    var result = new List<ImageSource>();
    var matches = MediaRefRegex.Matches(content);

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
    var filtered = string.IsNullOrWhiteSpace(query)
        ? _allNotes
        : _allNotes.Where(n =>
            n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            n.Preview.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

    CollectionMerge.MergeInto(Notes, filtered);
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
    _selectedNoteId = vm.Note.Id;
    NoteSelected?.Invoke(this, vm.Note);
  }

  private async void OnNewNoteButtonClicked(object sender, EventArgs e)
  {
    if (string.IsNullOrEmpty(_currentFolderId))
    {
      _toastService.Show("please select a folder");
      return;
    }

    var page = Application.Current!.Windows[0].Page!;
    var title = await page.DisplayPromptAsync("new note", "title:");
    if (!string.IsNullOrWhiteSpace(title))
    {
      var note = await _noteManager.CreateNoteAsync(title, _currentFolderId);
      await LoadNotesAsync(_currentFolderId);

      var created = Notes.FirstOrDefault(n => n.Note.Id == note.Id);
      if (created != null) SelectNote(created);
    }
  }

  private async void OnChangeNoteIconContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm) return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var icon = await IconSet.PickAsync(page);
    if (icon == null) return;

    vm.Note.Icon = icon;
    await _noteManager.UpdateNoteAsync(vm.Note);

    var idx = Notes.IndexOf(vm);
    var allIdx = _allNotes.IndexOf(vm);
    var isSelected = vm.IsSelected;
    var updated = new NoteViewModel(vm.Note, vm.PreviewImages) { IsSelected = isSelected };
    if (idx >= 0) Notes[idx] = updated;
    if (allIdx >= 0) _allNotes[allIdx] = updated;
  }

  private async void OnRenameNoteContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    var newTitle = await page.DisplayPromptAsync("rename note", "new name:", initialValue: vm.Title);
    if (string.IsNullOrWhiteSpace(newTitle) || newTitle == vm.Title) return;

    vm.Note.Title = newTitle;
    await _noteManager.UpdateNoteAsync(vm.Note);

    var isSelected = vm.IsSelected;
    var updated = new NoteViewModel(vm.Note, vm.PreviewImages) { IsSelected = isSelected };

    _allNotes.Remove(vm);
    int allIdx = _allNotes.Count;
    for (int i = 0; i < _allNotes.Count; i++)
      if (NaturalSortComparer.Instance.Compare(_allNotes[i].Title, updated.Title) > 0) { allIdx = i; break; }
    _allNotes.Insert(allIdx, updated);

    ApplySearch(SearchEntry.Text);

    if (isSelected)
      NoteSelected?.Invoke(this, vm.Note);
  }

  private async void OnNoteInfoContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    await page.DisplayAlert("note info", ItemInfoHelper.BuildNoteInfo(vm.Note), "ok");
  }

  private async void OnDeleteNoteContextMenuClicked(object sender, EventArgs e)
  {
    if (sender is not MenuFlyoutItem item || item.BindingContext is not NoteViewModel vm)
      return;
    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
    if (page == null) return;

    bool confirm = await page.DisplayAlert("delete note",
        $"delete \"{vm.Title}\"?", "delete", "cancel");
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
      if (_selectedNoteId == noteId) _selectedNoteId = null;
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
  public string Icon => Note.Icon;
  public string Preview => _preview ??= GetPreview();
  public string ModifiedString => Note.Modified.ToString("dd.MM HH:mm");

  private string? _preview;
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

  private static readonly System.Text.RegularExpressions.Regex CodeBlockRx = new(@"```[\s\S]*?```", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex ImageRx = new(@"!\[[^\]]*\]\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex LinkRx = new(@"\[([^\]]*)\]\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex HeaderRx = new(@"^#{1,6}\s+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
  private static readonly System.Text.RegularExpressions.Regex BoldRx = new(@"\*{1,3}([^*]*)\*{1,3}", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex ItalicRx = new(@"_{1,3}([^_]*)_{1,3}", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex InlineCodeRx = new(@"`([^`]+)`", System.Text.RegularExpressions.RegexOptions.Compiled);
  private static readonly System.Text.RegularExpressions.Regex ListMarkerRx = new(@"^[>*+\-]\s+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
  private static readonly System.Text.RegularExpressions.Regex NumberedListRx = new(@"^\d+\.\s+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
  private static readonly System.Text.RegularExpressions.Regex WhitespaceRx = new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

  private string GetPreview()
  {
    if (string.IsNullOrEmpty(Note.Content)) return "No text";

    var text = Note.Content;

    // strip fenced code blocks first
    text = CodeBlockRx.Replace(text, " ");
    // strip images
    text = ImageRx.Replace(text, "");
    // strip links — keep label
    text = LinkRx.Replace(text, "$1");
    // strip headers
    text = HeaderRx.Replace(text, "");
    // strip bold/italic
    text = BoldRx.Replace(text, "$1");
    text = ItalicRx.Replace(text, "$1");
    // strip inline code
    text = InlineCodeRx.Replace(text, "$1");
    // strip blockquotes and list markers
    text = ListMarkerRx.Replace(text, "");
    text = NumberedListRx.Replace(text, "");

    text = WhitespaceRx.Replace(text, " ").Trim();

    return text.Length > 80 ? text[..80] + "…" : text;
  }

  public event PropertyChangedEventHandler? PropertyChanged;
  protected void OnPropertyChanged([CallerMemberName] string name = "") =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
