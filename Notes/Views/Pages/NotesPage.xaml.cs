using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Notes;
using Notes.Services.Sync;
using System.Collections.ObjectModel;
using System.Linq;

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
  private Android.Views.ViewGroup? _actualCurrentContainer;
  private Android.Views.View? _nativeShadow;
  private float _shadowWidthPx;
  private float _density = 1f;
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
    var sorted = notes.OrderByDescending(n => n.Modified).ToList();
    if (IsNotesCollectionUnchanged(sorted)) return;
    Notes.Clear();
    foreach (var note in sorted)
      Notes.Add(note);
  }

  private bool IsNotesCollectionUnchanged(List<Note> sorted)
  {
    if (sorted.Count != Notes.Count) return false;
    for (int i = 0; i < sorted.Count; i++)
      if (sorted[i].Id != Notes[i].Id || sorted[i].Modified != Notes[i].Modified) return false;
    return true;
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
    float d = Math.Max(0, dx);
#if ANDROID
    if (_actualCurrentContainer == null || _actualCurrentContainer.Handle == IntPtr.Zero)
      ShowPreviousPage();
    if (_actualCurrentContainer != null)
    {
      float dPx = d * _density;
      _actualCurrentContainer.TranslationX = dPx;
      if (_nativeShadow != null)
      {
        _nativeShadow.TranslationX = dPx - _shadowWidthPx;
        _nativeShadow.Alpha = Math.Min(1f, d / 200f);
      }
      return;
    }
#endif
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
#if ANDROID
    if (_actualCurrentContainer != null)
    {
      float screenWidthPx = (float)DeviceDisplay.MainDisplayInfo.Width;
      await NativeTranslateAsync(_actualCurrentContainer, screenWidthPx + 20, 220,
          _nativeShadow, screenWidthPx + 20 - _shadowWidthPx, 0f);
      _actualCurrentContainer = null;
      if (_nativeShadow != null)
      {
        (_nativeShadow.Parent as Android.Views.ViewGroup)?.RemoveView(_nativeShadow);
        _nativeShadow.Dispose();
        _nativeShadow = null;
      }
      await Shell.Current.GoToAsync("..", false);
      return;
    }
#endif
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
#if ANDROID
    if (_actualCurrentContainer != null)
    {
      await NativeTranslateAsync(_actualCurrentContainer, 0, 300,
          _nativeShadow, -_shadowWidthPx, 0f);
      HidePreviousPage();
      return;
    }
#endif
    await Task.WhenAll(
      RootGrid.TranslateTo(0, 0, 300, Easing.SpringOut),
      SwipeShadow.FadeTo(0, 180)
    );
    SwipeShadow.TranslationX = -24;
  }

#if ANDROID
  private void ShowPreviousPage()
  {
    // Clean up any stale state from a previous call before re-searching.
    if (_prevPageView != null && _prevPageView.Handle != IntPtr.Zero)
      _prevPageView.Visibility = Android.Views.ViewStates.Gone;
    _prevPageView = null;
    _actualCurrentContainer = null;

    if (Handler?.PlatformView is not Android.Views.View handlerView) return;

    Android.Views.View cursor = handlerView;
    for (int level = 0; level < 8; level++)
    {
      if (cursor.Parent is not Android.Views.ViewGroup parent) break;

      Android.Views.ViewGroup? currentContainer = null;
      Android.Views.ViewGroup? prevContainer = null;

      if (cursor.Visibility == Android.Views.ViewStates.Visible && cursor is Android.Views.ViewGroup cursorVg)
      {
        // cursor IS the actual current page container
        currentContainer = cursorVg;
        for (int i = parent.ChildCount - 1; i >= 0; i--)
        {
          if (parent.GetChildAt(i) is Android.Views.ViewGroup vg && vg != cursor
              && vg.Visibility == Android.Views.ViewStates.Gone)
          { prevContainer = vg; break; }
        }
      }
      else
      {
        // cursor is a stale Gone container — find visible sibling (actual current)
        // then first Gone sibling after it in reverse order (skipping cursor) = previous page
        for (int i = parent.ChildCount - 1; i >= 0; i--)
        {
          if (parent.GetChildAt(i) is not Android.Views.ViewGroup vg || vg == cursor) continue;
          if (vg.Visibility == Android.Views.ViewStates.Visible && currentContainer == null)
            currentContainer = vg;
          else if (vg.Visibility == Android.Views.ViewStates.Gone && currentContainer != null)
          { prevContainer = vg; break; }
        }
      }

      if (currentContainer != null && prevContainer != null)
      {
        _actualCurrentContainer = currentContainer;
        prevContainer.Visibility = Android.Views.ViewStates.Visible;
        _prevPageView = prevContainer;
        AddNativeShadow();
        return;
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
    if (_actualCurrentContainer != null)
    {
      _actualCurrentContainer.TranslationX = 0;
      _actualCurrentContainer = null;
    }
    if (_nativeShadow != null)
    {
      (_nativeShadow.Parent as Android.Views.ViewGroup)?.RemoveView(_nativeShadow);
      _nativeShadow.Dispose();
      _nativeShadow = null;
    }
  }

  private void AddNativeShadow()
  {
    if (_actualCurrentContainer?.Parent is not Android.Views.ViewGroup parent) return;
    var ctx = Android.App.Application.Context!;
    _density = ctx.Resources!.DisplayMetrics!.Density;
    _shadowWidthPx = 24f * _density;
    var gradient = new Android.Graphics.Drawables.GradientDrawable(
        Android.Graphics.Drawables.GradientDrawable.Orientation.LeftRight,
        new[] { 0x00000000, unchecked((int)0x55000000) });
    var shadow = new Android.Views.View(ctx);
    shadow.Background = gradient;
    shadow.Alpha = 0f;
    shadow.TranslationX = -_shadowWidthPx;
    parent.AddView(shadow, new Android.Views.ViewGroup.LayoutParams(
        (int)_shadowWidthPx, Android.Views.ViewGroup.LayoutParams.MatchParent));
    _nativeShadow = shadow;
  }

  private static Task NativeTranslateAsync(Android.Views.View view, float toX, long ms,
      Android.Views.View? shadow = null, float shadowToX = 0, float shadowAlpha = 0)
  {
    var tcs = new TaskCompletionSource<bool>();
    view.Animate().TranslationX(toX).SetDuration(ms)
        .WithEndAction(new Java.Lang.Runnable(() => tcs.TrySetResult(true)))
        .Start();
    shadow?.Animate().TranslationX(shadowToX).Alpha(shadowAlpha).SetDuration(ms).Start();
    return tcs.Task;
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

  private async void OnChangeNoteIconMenuClicked(object sender, EventArgs e)
  {
    if (sender is MenuFlyoutItem item && item.BindingContext is Note note)
      await ChangeNoteIconAsync(note);
  }

  private async void OnRenameNoteMenuClicked(object sender, EventArgs e)
  {
    if (sender is MenuFlyoutItem item && item.BindingContext is Note note)
      await RenameNoteAsync(note);
  }

  private async void OnDeleteNoteMenuClicked(object sender, EventArgs e)
  {
    if (sender is MenuFlyoutItem item && item.BindingContext is Note note)
      await DeleteNoteAsync(note);
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