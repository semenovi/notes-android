using Notes.Helpers;
using Notes.Models;
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
  private readonly Services.ToastService _toastService;
  public ObservableCollection<object> Items { get; } = new ObservableCollection<object>();
  private CancellationTokenSource? _loadCts;
  private bool _isSwipingBack;
  // parent of the currently open folder — drop target for the "move up" zone.
  // Null both before the first load and when the current folder is itself a root.
  private string? _currentParentId;
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
      LoadItemsAsync().ConfigureAwait(false);
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

  private bool _isDragActive;
  public bool IsDragActive
  {
    get => _isDragActive;
    set
    {
      if (_isDragActive == value) return;
      _isDragActive = value;
      OnPropertyChanged();
      AnimateMoveUpZone(value);
      AnimateFab(!value);
    }
  }

  private async void AnimateMoveUpZone(bool show)
  {
    MoveUpZone.InputTransparent = !show;
    if (show)
      await Task.WhenAll(
          MoveUpZone.FadeTo(1, 160, Easing.CubicOut),
          MoveUpZone.TranslateTo(0, 0, 160, Easing.CubicOut));
    else
      await Task.WhenAll(
          MoveUpZone.FadeTo(0, 120, Easing.CubicIn),
          MoveUpZone.TranslateTo(0, 30, 120, Easing.CubicIn));
  }

  // Hidden while dragging: it sits where the "move up" zone now lives, and it's
  // not something you'd want to hit by accident mid-drag anyway. Animated rather
  // than an IsVisible binding so it shrinks/fades away instead of popping.
  private async void AnimateFab(bool show)
  {
    AddNoteButton.InputTransparent = !show;
    if (show)
      await Task.WhenAll(
          AddNoteButton.FadeTo(1, 160, Easing.CubicOut),
          AddNoteButton.ScaleTo(1, 160, Easing.CubicOut));
    else
      await Task.WhenAll(
          AddNoteButton.FadeTo(0, 120, Easing.CubicIn),
          AddNoteButton.ScaleTo(0.6, 120, Easing.CubicIn));
  }

  private bool _isEmpty = true;
  public bool IsEmpty
  {
    get => _isEmpty;
    set
    {
      if (_isEmpty == value) return;
      _isEmpty = value;
      OnPropertyChanged();
    }
  }

  public NotesPage(NoteManager noteManager, FolderManager folderManager,
      ReactiveSyncService reactiveSync, Services.ToastService toastService)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _folderManager = folderManager;
    _reactiveSync = reactiveSync;
    _toastService = toastService;
    Items.CollectionChanged += (_, _) => IsEmpty = Items.Count == 0;
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
    global::Notes.Platforms.Android.DragReorderGesture.OnLongPress = OnNativeLongPress;
    global::Notes.Platforms.Android.DragReorderGesture.OnMove = OnNativeDragMove;
    global::Notes.Platforms.Android.DragReorderGesture.OnEnd = OnNativeDragEnd;
    global::Notes.Platforms.Android.DragReorderGesture.OnCancel = OnNativeDragCancel;
#endif
    _reactiveSync.RemoteChangesApplied += OnRemoteChangesApplied;
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    // Belt-and-braces: if the page is left mid-drag (back button, notification, etc.)
    // and the drag never got a proper end, don't leave the "move up" zone stuck visible.
    IsDragActive = false;
#if ANDROID
    if (!_isSwipingBack)
      HidePreviousPage();
    global::Notes.Platforms.Android.SwipeBackGesture.OnProgress = null;
    global::Notes.Platforms.Android.SwipeBackGesture.OnEnd = null;
    global::Notes.Platforms.Android.SwipeBackGesture.OnCancel = null;
    global::Notes.Platforms.Android.DragReorderGesture.OnLongPress = null;
    global::Notes.Platforms.Android.DragReorderGesture.OnMove = null;
    global::Notes.Platforms.Android.DragReorderGesture.OnEnd = null;
    global::Notes.Platforms.Android.DragReorderGesture.OnCancel = null;
    _draggedItem = null;
    _draggedRowOrigin = null;
#endif
    _reactiveSync.RemoteChangesApplied -= OnRemoteChangesApplied;
  }

  private async void OnRemoteChangesApplied() => await LoadItemsAsync();

  private async Task LoadItemsAsync()
  {
    if (string.IsNullOrEmpty(FolderId))
      return;
    _loadCts?.Cancel();
    var cts = new CancellationTokenSource();
    _loadCts = cts;
    var currentFolder = await _folderManager.GetFolderAsync(FolderId);
    var folders = await _folderManager.GetFoldersAsync(FolderId);
    var notes = await _noteManager.GetNotesAsync(FolderId);
    if (cts.IsCancellationRequested) return;
    _currentParentId = currentFolder?.ParentId;
    var combined = folders.OrderBy(f => f.Name, NaturalSortComparer.Instance).Cast<object>()
        .Concat(notes.OrderBy(n => n.Title, NaturalSortComparer.Instance).Cast<object>())
        .ToList();
    DiffUpdateItems(combined);
  }

  // Merges newItems (already in folders-then-notes, name-sorted order) into Items:
  // unchanged entries keep their spot, changed ones are replaced in place, new
  // ones are inserted at their sorted position instead of just appended.
  private void DiffUpdateItems(List<object> newItems)
  {
    var newIds = newItems.Select(GetId).ToHashSet();

    for (int i = Items.Count - 1; i >= 0; i--)
      if (!newIds.Contains(GetId(Items[i])))
        Items.RemoveAt(i);

    for (int i = 0; i < newItems.Count; i++)
    {
      var newItem = newItems[i];
      string id = GetId(newItem);

      int idx = -1;
      for (int j = i; j < Items.Count; j++)
        if (GetId(Items[j]) == id) { idx = j; break; }

      if (idx < 0)
        Items.Insert(i, newItem);
      else
      {
        if (GetModified(Items[idx]) != GetModified(newItem))
          Items[idx] = newItem;
        if (idx != i)
          Items.Move(idx, i);
      }
    }
  }

  private static string GetId(object item) => item switch
  {
    Folder f => f.Id,
    Note n => n.Id,
    _ => string.Empty
  };

  private static DateTime GetModified(object item) => item switch
  {
    Folder f => f.Modified,
    Note n => n.Modified,
    _ => default
  };

  private async void OnAddNoteClicked(object sender, EventArgs e)
  {
    string noteTitle = await DisplayPromptAsync("new note", "enter note title:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(noteTitle))
    {
      var newNote = await _noteManager.CreateNoteAsync(noteTitle, FolderId);
      await LoadItemsAsync();
      await NavigateToNoteEditor(newNote);
    }
  }

  private async void OnAddSubfolderClicked(object sender, EventArgs e)
  {
    string folderName = await DisplayPromptAsync("new folder", "enter folder name:", initialValue: "");

    if (!string.IsNullOrWhiteSpace(folderName))
    {
      await _folderManager.CreateFolderAsync(folderName, FolderId);
      await LoadItemsAsync();
    }
  }

  private async void OnFolderItemTapped(object sender, TappedEventArgs e)
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

  private static void DragLog(string message) => System.Diagnostics.Debug.WriteLine($"[dragdrop] {message}");

  // Hover state, shared between folder rows and the "move up" zone — kept simple
  // (single tracked row/flag) since only one thing can be hovered at a time.
  private VisualElement? _hoveredFolderRow;
  private bool _moveUpHovered;

  private const double MoveUpZoneRestHeight = 56;
  private const double MoveUpZoneHoverHeight = 68;

  // Animates HeightRequest rather than TranslationY: the zone is bottom-anchored
  // (VerticalOptions="End"), so growing its height keeps the bottom edge fixed
  // and only the top edge moves up — translating the whole bar instead would
  // shift the bottom edge too, which reads as illogical for a "grows on hover"
  // affordance.
  private static void AnimateMoveUpHeight(VisualElement owner, Border zone, double to)
  {
    new Animation(v => zone.HeightRequest = v, zone.HeightRequest, to)
        .Commit(owner, "MoveUpHeightAnim", length: 120, easing: Easing.CubicOut);
  }

#if ANDROID
  // Drag is driven entirely by raw touch tracking from MainActivity (see
  // DragReorderGesture) instead of Android's native View.startDragAndDrop —
  // that native API has a long-standing platform bug where a live drag session
  // crashes (ConcurrentModificationException in ViewGroup.dispatchDragEvent) if
  // the view hierarchy changes anywhere nearby, which a reactive MAUI UI does
  // constantly (highlight animations, list reloads, IsVisible bindings). Manual
  // touch tracking never touches that code path.
  private object? _draggedItem;
  private View? _draggedRowOrigin;

  private enum DropTargetKind { None, Folder, MoveUp }

  private void OnNativeLongPress(float rawX, float rawY)
  {
    var (row, item) = HitTestRow(rawX, rawY);
    DragLog($"LongPress row={row?.GetType().Name} item={item?.GetType().Name}");
    if (row == null || item == null) return;

    _draggedItem = item;
    _draggedRowOrigin = row;
    IsDragActive = true;
    ShowDragGhost(item, rawX, rawY);
  }

  private void OnNativeDragMove(float rawX, float rawY)
  {
    if (_draggedItem == null) return;
    PositionGhost(rawX, rawY);
    UpdateHover(rawX, rawY);
  }

  private async void OnNativeDragEnd(float rawX, float rawY)
  {
    if (_draggedItem == null) return;
    var item = _draggedItem;
    _draggedItem = null;
    _draggedRowOrigin = null;
    HideDragGhost();

    var (kind, targetFolder) = HitTestDropTarget(rawX, rawY);
    // ClearHoverState animates the hovered row/zone straight back to rest (scale
    // 1 / resting height) — that alone reads as "dropped here", no extra pulse
    // needed on top of it.
    ClearHoverState();
    IsDragActive = false;

    DragLog($"DragEnd kind={kind} item={item.GetType().Name}");
    if (kind == DropTargetKind.Folder && targetFolder != null)
    {
      await MoveItemAsync(item, targetFolder.Id);
    }
    else if (kind == DropTargetKind.MoveUp)
    {
      if (item is Note && _currentParentId == null)
        _toastService.Show("notes can't be moved outside a folder");
      else
        await MoveItemAsync(item, _currentParentId);
    }
  }

  private void OnNativeDragCancel()
  {
    DragLog("DragCancel");
    _draggedItem = null;
    _draggedRowOrigin = null;
    HideDragGhost();
    ClearHoverState();
    IsDragActive = false;
  }

  private void UpdateHover(float rawX, float rawY)
  {
    var (kind, folder) = HitTestDropTarget(rawX, rawY);

    VisualElement? newHoverRow = null;
    if (kind == DropTargetKind.Folder && folder != null)
      foreach (var child in ItemsStack.Children)
        if (child is VisualElement v && ReferenceEquals(v.BindingContext, folder)) { newHoverRow = v; break; }

    if (!ReferenceEquals(newHoverRow, _hoveredFolderRow))
    {
      _hoveredFolderRow?.ScaleTo(1, 120, Easing.CubicOut);
      _hoveredFolderRow = newHoverRow;
      // Kept subtle on purpose: the row scales from its center, which doesn't line
      // up with the left-aligned icon/text — a bigger bump makes that drift obvious.
      _hoveredFolderRow?.ScaleTo(1.02, 120, Easing.CubicOut);
    }

    bool overMoveUp = kind == DropTargetKind.MoveUp;
    if (overMoveUp != _moveUpHovered)
    {
      _moveUpHovered = overMoveUp;
      AnimateMoveUpHeight(this, MoveUpZone, overMoveUp ? MoveUpZoneHoverHeight : MoveUpZoneRestHeight);
    }
  }

  private void ClearHoverState()
  {
    if (_hoveredFolderRow != null)
    {
      _hoveredFolderRow.ScaleTo(1, 120, Easing.CubicOut);
      _hoveredFolderRow = null;
    }
    if (_moveUpHovered)
    {
      _moveUpHovered = false;
      MoveUpZone.HeightRequest = MoveUpZoneRestHeight;
    }
  }

  private (View? row, object? item) HitTestRow(float rawX, float rawY)
  {
    foreach (var child in ItemsStack.Children)
    {
      if (child is not View view) continue;
      if (ContainsScreenPoint(view, rawX, rawY))
        return (view, view.BindingContext);
    }
    return (null, null);
  }

  private (DropTargetKind kind, Folder? folder) HitTestDropTarget(float rawX, float rawY)
  {
    if (MoveUpZone.Opacity > 0.05 && ContainsScreenPoint(MoveUpZone, rawX, rawY))
      return (DropTargetKind.MoveUp, null);

    foreach (var child in ItemsStack.Children)
    {
      if (child is not View view || view == _draggedRowOrigin) continue;
      if (view.BindingContext is not Folder folder) continue;
      if (ContainsScreenPoint(view, rawX, rawY))
        return (DropTargetKind.Folder, folder);
    }
    return (DropTargetKind.None, null);
  }

  private static bool ContainsScreenPoint(VisualElement view, float rawX, float rawY)
  {
    if (view.Handler?.PlatformView is not Android.Views.View native || !native.IsShown) return false;
    var loc = new int[2];
    native.GetLocationOnScreen(loc);
    return rawX >= loc[0] && rawX <= loc[0] + native.Width
        && rawY >= loc[1] && rawY <= loc[1] + native.Height;
  }

  private void ShowDragGhost(object item, float rawX, float rawY)
  {
    DragGhostLabel.Text = item switch { Folder f => f.Name, Note n => n.Title, _ => "" };
    DragGhostIcon.Text = item switch { Folder f => f.Icon, Note n => n.Icon, _ => "" };
    // Match each row template's own icon color: notes tint their icon (Primary/
    // PrimaryDark), folders use the label's default color. Without this the ghost's
    // icon used the MaterialIconLabel style's flat default for both, which doesn't
    // match either original row.
    if (item is Note)
    {
      string key = Application.Current?.RequestedTheme == AppTheme.Dark ? "PrimaryDark" : "Primary";
      if (Application.Current?.Resources.TryGetValue(key, out var color) == true)
        DragGhostIcon.TextColor = (Color)color;
    }
    else
    {
      DragGhostIcon.ClearValue(Label.TextColorProperty);
    }
    DragGhost.IsVisible = true;
    DragGhost.Opacity = 0;
    DragGhost.Scale = 0.95;
    PositionGhost(rawX, rawY);
    DragGhost.FadeTo(0.94, 100);
    DragGhost.ScaleTo(1, 100, Easing.CubicOut);
  }

  private void PositionGhost(float rawX, float rawY)
  {
    // TranslationX/Y on DragGhost are relative to RootGrid's own on-screen origin,
    // not the screen's — RootGrid starts below the page's toolbar/status bar, so
    // rawX/rawY (screen-absolute) need that origin subtracted first, the same way
    // ContainsScreenPoint compares against each row's own on-screen location.
    if (RootGrid.Handler?.PlatformView is not Android.Views.View rootNative) return;
    var loc = new int[2];
    rootNative.GetLocationOnScreen(loc);
    float density = Android.App.Application.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
    // Centered on the touch point — half of WidthRequest (260) and the row's
    // typical height (Padding 20,10 + 24pt icon ≈ 44dp).
    DragGhost.TranslationX = (rawX - loc[0]) / density - 130;
    DragGhost.TranslationY = (rawY - loc[1]) / density - 22;
  }

  private void HideDragGhost() => DragGhost.IsVisible = false;
#endif

  // targetParentId == null means "move to the top level" — only ever valid for a folder.
  // Returns whether an item was actually relocated (false for a no-op or rejected move).
  private async Task<bool> MoveItemAsync(object draggedItem, string? targetParentId)
  {
    DragLog($"MoveItemAsync draggedItem={draggedItem?.GetType().Name} targetParentId={targetParentId}");
    if (draggedItem is Note note)
    {
      if (targetParentId == null || note.FolderId == targetParentId)
      {
        DragLog("MoveItemAsync: no-op for note (same folder or null target)");
        return false;
      }
      note.FolderId = targetParentId;
      await _noteManager.UpdateNoteAsync(note);
      DragLog($"MoveItemAsync: note '{note.Title}' moved to folder {targetParentId}");
      await LoadItemsAsync();
      return true;
    }

    if (draggedItem is Folder folder)
    {
      if (folder.Id == targetParentId || folder.ParentId == targetParentId)
      {
        DragLog("MoveItemAsync: no-op for folder (self or same parent)");
        return false;
      }

      var descendants = await _folderManager.GetDescendantFoldersAsync(folder.Id);
      if (targetParentId != null && descendants.Any(d => d.Id == targetParentId))
      {
        DragLog("MoveItemAsync: rejected, would create a cycle");
        _toastService.Show("can't move a folder into its own subfolder");
        return false;
      }

      await _folderManager.MoveFolderAsync(folder.Id, targetParentId);
      DragLog($"MoveItemAsync: folder '{folder.Name}' moved to parent {targetParentId}");
      await LoadItemsAsync();
      return true;
    }

    return false;
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

    var newName = await DisplayPromptAsync("rename folder", "new name:", initialValue: folder.Name);
    if (string.IsNullOrWhiteSpace(newName) || newName == folder.Name) return;

    folder.Name = newName;
    folder.Modified = DateTime.UtcNow;
    await _folderManager.UpdateFolderAsync(folder);
    FolderName = newName;
  }

  private async void OnFolderInfoClicked(object sender, EventArgs e)
  {
    var folder = await _folderManager.GetFolderAsync(FolderId);
    if (folder == null) return;

    var notes = await _noteManager.GetNotesAsync(FolderId);
    await DisplayAlert("folder info", ItemInfoHelper.BuildFolderInfo(folder, notes), "ok");
  }

  private async void OnDeleteFolderClicked(object sender, EventArgs e)
  {
    var descendants = await _folderManager.GetDescendantFoldersAsync(FolderId);
    string warning = descendants.Count > 0
        ? $"delete \"{FolderName}\", its {descendants.Count} {(descendants.Count == 1 ? "subfolder" : "subfolders")} and all notes inside?"
        : $"delete \"{FolderName}\" and all notes inside?";
    bool confirm = await DisplayAlert("delete folder", warning, "delete", "cancel");
    if (!confirm) return;

    foreach (var folderId in descendants.Select(f => f.Id).Append(FolderId))
    {
      var notes = await _noteManager.GetNotesAsync(folderId);
      foreach (var note in notes)
        await _noteManager.DeleteNoteAsync(note.Id);
    }
    foreach (var descendant in descendants)
      await _folderManager.DeleteFolderAsync(descendant.Id);

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

public class NotesPageItemTemplateSelector : DataTemplateSelector
{
  public DataTemplate FolderTemplate { get; set; }
  public DataTemplate NoteTemplate { get; set; }

  protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    => item is Folder ? FolderTemplate : NoteTemplate;
}