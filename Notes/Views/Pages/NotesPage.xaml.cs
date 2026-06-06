using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;

namespace Notes.Views.Pages;

[QueryProperty(nameof(FolderId), "FolderId")]
[QueryProperty(nameof(FolderName), "FolderName")]
public partial class NotesPage : ContentPage
{
  private readonly NoteManager _noteManager;
  private readonly FolderManager _folderManager;
  private readonly ReactiveSyncService _reactiveSync;
  private readonly ProgressNotificationService _progressService;
  public ObservableCollection<Note> Notes { get; } = new ObservableCollection<Note>();
  private CancellationTokenSource? _loadCts;
  private bool _isSwipingBack;
#if ANDROID
  private Android.Views.View? _prevPageView;
#endif

  private string _folderId;
  public string FolderId
  {
    get => _folderId;
    set
    {
      _folderId = value;
      LoadNotesAsync().ConfigureAwait(false);
    }
  }

  private string _folderName;
  public string FolderName
  {
    get => _folderName;
    set
    {
      _folderName = value;
      OnPropertyChanged();
    }
  }

  public NotesPage(NoteManager noteManager, FolderManager folderManager,
      ReactiveSyncService reactiveSync, ProgressNotificationService progressService)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _folderManager = folderManager;
    _reactiveSync = reactiveSync;
    _progressService = progressService;
    NotesCollection.ItemsSource = Notes;
    BindingContext = this;
  }

  protected override void OnAppearing()
  {
    base.OnAppearing();
    _isSwipingBack = false;
    RootGrid.TranslationX = 0;
    SwipeShadow.TranslationX = -24;
    SwipeShadow.Opacity = 0;
#if ANDROID
    ShowPreviousPage();
    global::Notes.Platforms.Android.SwipeBackGesture.OnProgress = OnSwipeProgress;
    global::Notes.Platforms.Android.SwipeBackGesture.OnEnd = OnSwipeEnd;
    global::Notes.Platforms.Android.SwipeBackGesture.OnCancel = () => _ = SpringBackAsync();
#endif
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
    _progressService.ShowRequested += PageProgress.ShowProgress;
    _progressService.UpdateRequested += PageProgress.UpdateProgress;
    _progressService.HideRequested += PageProgress.HideProgress;
    if (_progressService.Current != null)
      PageProgress.ShowProgress(_progressService.Current);
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
#if ANDROID
    if (!_isSwipingBack)
      HidePreviousPage();
    global::Notes.Platforms.Android.SwipeBackGesture.OnProgress = null;
    global::Notes.Platforms.Android.SwipeBackGesture.OnEnd = null;
    global::Notes.Platforms.Android.SwipeBackGesture.OnCancel = null;
#endif
    _reactiveSync.RemoteChangesApplied -= OnRemoteChangesApplied;
    _progressService.ShowRequested -= PageProgress.ShowProgress;
    _progressService.UpdateRequested -= PageProgress.UpdateProgress;
    _progressService.HideRequested -= PageProgress.HideProgress;
    PageProgress.Reset();
  }

  private async void OnRemoteChangesApplied() => await LoadNotesAsync();

  private async Task LoadNotesAsync()
  {
    if (string.IsNullOrEmpty(FolderId))
      return;
    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var notes = await _noteManager.GetNotesAsync(FolderId);
    if (cts.IsCancellationRequested) return;
    Notes.Clear();
    foreach (var note in notes)
      Notes.Add(note);
  }

  private async void OnAddNoteClicked(object sender, EventArgs e)
  {
    string noteTitle = await DisplayPromptAsync("New Note", "Enter note title:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(noteTitle))
    {
      var newNote = await _noteManager.CreateNoteAsync(noteTitle, FolderId);
      Notes.Add(newNote);

      await NavigateToNoteEditor(newNote);
    }
  }

  private void OnSwipeProgress(float dx)
  {
    double d = Math.Max(0, dx);
    RootGrid.TranslationX = d;
    SwipeShadow.TranslationX = d - 24;
    SwipeShadow.Opacity = 1.0;
  }

  private void OnSwipeEnd(float dx)
  {
    var screenWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
    if (dx > screenWidth * 0.35)
      _ = CompleteSwipeBackAsync();
    else
      _ = SpringBackAsync();
  }

  private async Task CompleteSwipeBackAsync()
  {
    _isSwipingBack = true;
    var screenWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
    await Task.WhenAll(
      RootGrid.TranslateTo(screenWidth + 20, 0, 220, Easing.CubicIn),
      SwipeShadow.TranslateTo(screenWidth - 4, 0, 220, Easing.CubicIn)
    );
    BackgroundColor = null;
    await Shell.Current.GoToAsync("..", false);
  }

  private async Task SpringBackAsync()
  {
    _isSwipingBack = false;
    await Task.WhenAll(
      RootGrid.TranslateTo(0, 0, 300, Easing.SpringOut),
      SwipeShadow.FadeTo(0, 180)
    );
    SwipeShadow.TranslationX = -24;
  }

#if ANDROID
  private void ShowPreviousPage()
  {
    if (Handler?.PlatformView is not Android.Views.View currentView) return;

    // Walk up the native view tree looking for a GONE sibling — that's the previous page's fragment view
    Android.Views.View cursor = currentView;
    for (int level = 0; level < 6; level++)
    {
      if (cursor.Parent is not Android.Views.ViewGroup parent) break;
      for (int i = 0; i < parent.ChildCount; i++)
      {
        var sibling = parent.GetChildAt(i);
        if (sibling == null || sibling == cursor) continue;
        if (sibling.Visibility == Android.Views.ViewStates.Gone && sibling is Android.Views.ViewGroup)
        {
          sibling.Visibility = Android.Views.ViewStates.Visible;
          _prevPageView = sibling;
          // Clear native background so MAUI's transparent ContentPage actually shows through
          currentView.Background = null;
          BackgroundColor = Colors.Transparent;
          return;
        }
      }
      cursor = parent;
    }
  }

  private void HidePreviousPage()
  {
    if (_prevPageView != null)
    {
      _prevPageView.Visibility = Android.Views.ViewStates.Gone;
      _prevPageView = null;
    }
    BackgroundColor = null;
  }
#endif

  private async void OnNoteTapped(object sender, TappedEventArgs e)
  {
    if (sender is View view && view.BindingContext is Note note)
    {
      await view.ScaleTo(0.96, 80);
      await view.ScaleTo(1.0, 80);
      await NavigateToNoteView(note);
    }
  }

  private async Task ChangeNoteIconAsync(Note note)
  {
    var icon = await IconSet.PickAsync(this);
    if (icon == null) return;
    note.Icon = icon;
    await _noteManager.UpdateNoteAsync(note);
    await LoadNotesAsync();
  }

  private async Task RenameNoteAsync(Note note)
  {
    var newTitle = await DisplayPromptAsync("Rename Note", "New name:", initialValue: note.Title);
    if (string.IsNullOrWhiteSpace(newTitle) || newTitle == note.Title) return;
    note.Title = newTitle;
    await _noteManager.UpdateNoteAsync(note);
    await LoadNotesAsync();
  }

  private async Task DeleteNoteAsync(Note note)
  {
    bool confirm = await DisplayAlert("Delete Note", $"Delete \"{note.Title}\"?", "Delete", "Cancel");
    if (!confirm) return;
    await _noteManager.DeleteNoteAsync(note.Id);
    Notes.Remove(note);
  }

  private async void OnChangeFolderIconClicked(object sender, EventArgs e)
  {
    var folder = await _folderManager.GetFolderAsync(FolderId);
    if (folder == null) return;

    var icon = await IconSet.PickAsync(this);
    if (icon == null) return;

    folder.Icon = icon;
    folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(folder);
  }

  private async void OnRenameFolderClicked(object sender, EventArgs e)
  {
    var folder = await _folderManager.GetFolderAsync(FolderId);
    if (folder == null) return;

    var newName = await DisplayPromptAsync("Rename Folder", "New name:", initialValue: folder.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == folder.Name) return;

    folder.Name = newName;
    folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(folder);
    FolderName = newName;
  }

  private async void OnDeleteFolderClicked(object sender, EventArgs e)
  {
    bool confirm = await DisplayAlert("Delete Folder",
        $"Delete \"{FolderName}\" and all notes inside?", "Delete", "Cancel");
    if (!confirm) return;

    var notes = await _noteManager.GetNotesAsync(FolderId);
    foreach (var note in notes)
      await _noteManager.DeleteNoteAsync(note.Id);

    await _folderManager.DeleteFolderAsync(FolderId);
    await Shell.Current.GoToAsync("..");
  }

  private async Task NavigateToNoteView(Note note)
  {
    var navigationParameter = new Dictionary<string, object>
    {
      { "NoteId", note.Id }
    };

    await Shell.Current.GoToAsync(nameof(NoteViewPage), navigationParameter);
  }

  private async Task NavigateToNoteEditor(Note note)
  {
    var navigationParameter = new Dictionary<string, object>
    {
      { "NoteId", note.Id }
    };

    await Shell.Current.GoToAsync(nameof(NoteEditorPage), navigationParameter);
  }
}