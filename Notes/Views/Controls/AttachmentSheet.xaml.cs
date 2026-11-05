using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Notes.Helpers;
using Notes.Services.Media;

namespace Notes.Views.Controls;

public partial class AttachmentSheet : ContentView
{
  private const int RecentsLimit = 40;
  private const int ThumbPx = 220;
  private const int ThumbConcurrency = 3;

  private readonly IRecentMediaProvider _recentMedia;
  private readonly ObservableCollection<RecentThumb> _recents = new();
  private readonly List<RecentThumb> _selectionOrder = new();

  private CancellationTokenSource? _lifetime;
  private TaskCompletionSource<AttachmentSheetResult>? _completion;
  private bool _closing;
#if ANDROID
  private static AttachmentSheet? _active;
  private readonly Dictionary<Android.Views.View, RecentThumb> _thumbViews = new();
  private readonly Dictionary<Android.Views.View, Android.Views.View> _thumbButtons = new();
  private readonly Dictionary<RecentThumb, Android.Views.View> _buttonsByThumb = new();
  private readonly Dictionary<RecentThumb, Button> _mauiButtons = new();
  private Android.Views.View.IOnKeyListener? _keyListener;
#endif

  public AttachmentSheet(IRecentMediaProvider recentMedia)
  {
    InitializeComponent();
    _recentMedia = recentMedia;
    RecentsView.ItemsSource = _recents;
    SizeChanged += (_, _) => ApplySheetWidth();
    Loaded += (_, _) => WireFocusKeys();
  }

  public async Task<AttachmentSheetResult> ShowAsync()
  {
    _recents.Clear();
    _selectionOrder.Clear();
#if ANDROID
    _thumbViews.Clear();
    _thumbButtons.Clear();
    _buttonsByThumb.Clear();
    _mauiButtons.Clear();
    _keyListener ??= new SheetKeyListener(OnSheetKey);
#endif
    _closing = false;
    _lifetime = new CancellationTokenSource();
    _completion = new TaskCompletionSource<AttachmentSheetResult>();

    ApplySheetWidth();
    IsVisible = true;
#if ANDROID
    _active = this;
#endif
    await AnimateInAsync();
    Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), FocusGallery);
    Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), ApplyFocusLinks);
    _ = LoadRecentsAsync(_lifetime.Token);

    var result = await _completion.Task;
    IsVisible = false;
#if ANDROID
    _active = null;
#endif
    return result;
  }

#if ANDROID
  private void WireFocusKeys()
  {
    WireKey(GalleryButton.Handler?.PlatformView as Android.Views.View);
    WireKey(FileButton.Handler?.PlatformView as Android.Views.View);
    WireKey(CameraButton.Handler?.PlatformView as Android.Views.View);
  }

  private void WireKey(Android.Views.View? view)
  {
    if (view == null)
      return;
    _keyListener ??= new SheetKeyListener(OnSheetKey);
    view.SetOnKeyListener(_keyListener);
  }

  private bool OnSheetKey(Android.Views.View view, Android.Views.Keycode key, Android.Views.KeyEvent e)
  {
    if (e.Action != Android.Views.KeyEventActions.Down)
      return false;

    if (view == GalleryButton.Handler?.PlatformView)
      return HandleSourceKey(key, AttachmentSource.Gallery);
    if (view == FileButton.Handler?.PlatformView)
      return HandleSourceKey(key, AttachmentSource.Files);
    if (view == CameraButton.Handler?.PlatformView)
      return HandleSourceKey(key, AttachmentSource.Camera);

    if (view == AddButton.Handler?.PlatformView)
    {
      if (key == Android.Views.Keycode.DpadCenter || key == Android.Views.Keycode.Enter)
      {
        OnAddTapped(null, EventArgs.Empty);
        return true;
      }

      if (key == Android.Views.Keycode.DpadUp)
      {
        FocusThumb(_recents.Count - 1);
        return true;
      }

      return key == Android.Views.Keycode.DpadDown;
    }

    if (!_thumbViews.TryGetValue(view, out var thumb))
      return false;

    int index = _recents.IndexOf(thumb);
    if (index < 0)
      return false;

    if (key == Android.Views.Keycode.DpadCenter || key == Android.Views.Keycode.Enter)
    {
      if (_mauiButtons.TryGetValue(thumb, out var button))
        OnRecentClicked(button, EventArgs.Empty);
      return true;
    }

    if (key == Android.Views.Keycode.DpadUp && index < 4)
    {
      if (_recents.Count > 0)
        RecentsView.ScrollTo(0, -1, ScrollToPosition.MakeVisible, false);
      var sources = SourceButtons.Handler?.PlatformView as Android.Views.ViewGroup;
      if (sources != null)
        FocusSource(sources, 2);
      return true;
    }

    int target = key switch
    {
      Android.Views.Keycode.DpadRight => index + 1,
      Android.Views.Keycode.DpadLeft when index % 4 != 0 => index - 1,
      Android.Views.Keycode.DpadDown => index + 4,
      Android.Views.Keycode.DpadUp => index - 4,
      _ => -1,
    };

    if (target >= 0 && target < _recents.Count)
    {
      FocusThumb(target);
      return true;
    }

    if (key == Android.Views.Keycode.DpadDown &&
        AddButton.Handler?.PlatformView is Android.Views.View add)
    {
      if (ActionButton.IsVisible)
      {
        if (_recents.Count > 0)
          RecentsView.ScrollTo(_recents.Count - 1, -1, ScrollToPosition.MakeVisible, false);
        add.RequestFocus();
      }
      else
      {
        FocusThumb(_recents.Count - 1);
      }
      return true;
    }

    return false;
  }

  private void FocusThumb(int index)
  {
    if (index < 0 || index >= _recents.Count)
      return;

    var thumb = _recents[index];
    RecentsView.ScrollTo(index, -1, ScrollToPosition.Center, false);
    FocusThumbAfterScroll(thumb, 0);
  }

  private void FocusThumbAfterScroll(RecentThumb thumb, int attempt)
  {
    Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
    {
      if (_buttonsByThumb.TryGetValue(thumb, out var view) && view.IsAttachedToWindow)
      {
        view.RequestFocus();
        return;
      }

      if (attempt < 10)
      {
        FocusThumbAfterScroll(thumb, attempt + 1);
        return;
      }

      if (RecentsView.Handler?.PlatformView is Android.Views.View grid)
        grid.RequestFocus();
    });
  }

  private bool HandleSourceKey(Android.Views.Keycode key, AttachmentSource source)
  {
    if (key == Android.Views.Keycode.DpadCenter || key == Android.Views.Keycode.Enter)
    {
      _ = DismissAsync(new AttachmentSourceChosen(source));
      return true;
    }

    if (key != Android.Views.Keycode.DpadDown)
      return false;

    if (_recents.Count > 0)
      RecentsView.ScrollTo(0, -1, ScrollToPosition.MakeVisible, false);
    var recents = RecentsView.Handler?.PlatformView as Android.Views.ViewGroup;
    var first = recents != null ? FirstFocusable(recents) : null;
    first?.RequestFocus();
    return first != null;
  }

  private void OnSourceKeyPress(object? sender, Android.Views.View.KeyEventArgs e)
  {
    if (e.Event.Action != Android.Views.KeyEventActions.Down)
      return;

    if (e.Event.KeyCode == Android.Views.Keycode.DpadCenter ||
        e.Event.KeyCode == Android.Views.Keycode.Enter)
    {
      if (sender == GalleryButton.Handler?.PlatformView)
        OnGalleryTapped(null, EventArgs.Empty);
      else if (sender == FileButton.Handler?.PlatformView)
        OnFilesTapped(null, EventArgs.Empty);
      else if (sender == CameraButton.Handler?.PlatformView)
        OnCameraTapped(null, EventArgs.Empty);
      e.Handled = true;
      return;
    }

    if (e.Event.KeyCode == Android.Views.Keycode.DpadDown)
    {
      var recents = RecentsView.Handler?.PlatformView as Android.Views.ViewGroup;
      var first = recents != null ? FirstFocusable(recents) : null;
      if (first != null)
      {
        first.RequestFocus();
        e.Handled = true;
      }
    }
  }

  private void OnSourceFocused(object? sender, FocusEventArgs e)
  {
    if (sender is Button button)
      WireKey(button.Handler?.PlatformView as Android.Views.View);
  }

  private void OnThumbFocused(object? sender, FocusEventArgs e)
  {
    if (sender is not Button button ||
        button.Handler?.PlatformView is not Android.Views.View view ||
        button.CommandParameter is not RecentThumb thumb)
      return;

    _thumbViews[view] = thumb;
    _buttonsByThumb[thumb] = view;
    _mauiButtons[thumb] = button;
    view.KeyPress -= OnThumbKeyPress;
    view.KeyPress += OnThumbKeyPress;
    WireKey(view);
  }

  private void OnThumbLoaded(object? sender, EventArgs e)
  {
    if (sender is not Button button ||
        button.Handler?.PlatformView is not Android.Views.View view ||
        button.CommandParameter is not RecentThumb thumb)
      return;

    _thumbViews[view] = thumb;
    _buttonsByThumb[thumb] = view;
    _mauiButtons[thumb] = button;
    view.KeyPress -= OnThumbKeyPress;
    view.KeyPress += OnThumbKeyPress;
    WireKey(view);

    var recents = RecentsView.Handler?.PlatformView as Android.Views.ViewGroup;
    Android.Views.View? current = view;
    while (recents != null && current.Parent is Android.Views.ViewGroup parent && parent != recents)
    {
      parent.Focusable = false;
      parent.DescendantFocusability = Android.Views.DescendantFocusability.AfterDescendants;
      current = parent;
    }

    if (current.Parent is Android.Views.View container)
    {
      _thumbViews[container] = thumb;
      _thumbButtons[container] = view;
      container.KeyPress -= OnThumbContainerKeyPress;
      container.KeyPress += OnThumbContainerKeyPress;
    }
  }

  private void OnThumbContainerKeyPress(object? sender, Android.Views.View.KeyEventArgs e)
  {
    if (e.Event.Action != Android.Views.KeyEventActions.Down ||
        sender is not Android.Views.View container)
      return;

    if (e.Event.KeyCode == Android.Views.Keycode.DpadCenter ||
        e.Event.KeyCode == Android.Views.Keycode.Enter)
    {
      if (_thumbButtons.TryGetValue(container, out var button))
      {
        button.PerformClick();
        e.Handled = true;
      }
      return;
    }

    if (e.Event.KeyCode != Android.Views.Keycode.DpadUp)
      return;

    if (!_thumbViews.TryGetValue(container, out var thumb))
      return;

    int index = _recents.IndexOf(thumb);
    if (index < 0 || index >= 4)
      return;

    var sourceGroup = SourceButtons.Handler?.PlatformView as Android.Views.ViewGroup;
    if (sourceGroup == null)
      return;

    FocusSource(sourceGroup, 2);
    e.Handled = true;
  }

  private void OnThumbKeyPress(object? sender, Android.Views.View.KeyEventArgs e)
  {
    if (e.Event.Action != Android.Views.KeyEventActions.Down ||
        sender is not Android.Views.View view ||
        !_thumbViews.TryGetValue(view, out var thumb))
      return;

    int index = _recents.IndexOf(thumb);
    if (index < 0)
      return;

    if (e.Event.KeyCode == Android.Views.Keycode.DpadCenter ||
        e.Event.KeyCode == Android.Views.Keycode.Enter)
    {
      if (_buttonsByThumb.TryGetValue(thumb, out var button))
      {
        OnRecentClicked(button, EventArgs.Empty);
        e.Handled = true;
      }
      return;
    }

    int target = -1;
    switch (e.Event.KeyCode)
    {
      case Android.Views.Keycode.DpadRight:
        target = index + 1;
        break;
      case Android.Views.Keycode.DpadLeft:
        if (index % 4 == 0)
        {
          var sources = SourceButtons.Handler?.PlatformView as Android.Views.ViewGroup;
          if (sources != null)
          {
            FocusSource(sources, 2);
            e.Handled = true;
          }
          return;
        }
        target = index - 1;
        break;
      case Android.Views.Keycode.DpadDown:
        if (index + 4 < _recents.Count)
          target = index + 4;
        else if (AddButton.Handler?.PlatformView is Android.Views.View add)
        {
          add.RequestFocus();
          e.Handled = true;
        }
        break;
      case Android.Views.Keycode.DpadUp:
        if (index < 4)
        {
          var sources = SourceButtons.Handler?.PlatformView as Android.Views.ViewGroup;
          if (sources != null)
          {
            FocusSource(sources, 2);
            e.Handled = true;
          }
          return;
        }
        target = index - 4;
        break;
    }

    if (target >= 0 && target < _recents.Count &&
        _buttonsByThumb.TryGetValue(_recents[target], out var targetView))
    {
      targetView.RequestFocus();
      e.Handled = true;
    }
  }
#endif

  private void ApplySheetWidth()
  {
    if (Width > 0)
      Sheet.WidthRequest = Math.Min(Width, 560);
  }

  private void FocusGallery()
  {
#if ANDROID
    if (GalleryButton.Handler?.PlatformView is Android.Views.View view)
    {
      view.Focusable = true;
      view.FocusableInTouchMode = true;
      view.RequestFocus();
    }
#endif
    GalleryButton.Focus();
  }

#if ANDROID
  private void ApplyFocusLinks()
  {
    var gallery = GalleryButton.Handler?.PlatformView as Android.Views.View;
    var file = FileButton.Handler?.PlatformView as Android.Views.View;
    var camera = CameraButton.Handler?.PlatformView as Android.Views.View;
    var recents = RecentsView.Handler?.PlatformView as Android.Views.View;
    if (gallery == null || file == null || camera == null || recents == null)
      return;

    if (recents.Id == Android.Views.View.NoId)
      recents.Id = Android.Views.View.GenerateViewId();
    if (camera.Id == Android.Views.View.NoId)
      camera.Id = Android.Views.View.GenerateViewId();

    gallery.NextFocusDownId = recents.Id;
    file.NextFocusDownId = recents.Id;
    camera.NextFocusDownId = recents.Id;
    recents.NextFocusUpId = camera.Id;
    WireKey(AddButton.Handler?.PlatformView as Android.Views.View);
  }
#endif

  private async Task AnimateInAsync()
  {
    double offset = await MeasuredSheetHeightAsync();
    Sheet.TranslationY = offset;
    await Task.WhenAll(
        Sheet.TranslateTo(0, 0, 280, Easing.CubicOut),
        Scrim.FadeTo(0.45, 200));
  }

  private async Task<double> MeasuredSheetHeightAsync()
  {
    for (int i = 0; i < 12 && Sheet.Height <= 0; i++)
      await Task.Delay(16);
    return Sheet.Height > 0 ? Sheet.Height : 480;
  }

  private async Task DismissAsync(AttachmentSheetResult result)
  {
    if (_closing)
      return;
    _closing = true;
    _lifetime?.Cancel();

    double offset = Sheet.Height > 0 ? Sheet.Height : 480;
    await Task.WhenAll(
        Sheet.TranslateTo(0, offset, 220, Easing.CubicIn),
        Scrim.FadeTo(0, 180));

    _lifetime?.Dispose();
    _completion?.TrySetResult(result);
  }

  private void OnScrimTapped(object? sender, TappedEventArgs e) => _ = DismissAsync(new AttachmentDismissed());

#if ANDROID
  public static bool HandleActiveKey(Android.Views.KeyEvent? e)
  {
    var sheet = _active;
    return sheet != null && sheet.HandleKey(e);
  }

  private bool HandleKey(Android.Views.KeyEvent? e)
  {
    if (e == null || e.Action != Android.Views.KeyEventActions.Down)
      return false;

    var focused = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.CurrentFocus;
    if (focused == null)
      return false;

    var sourceGroup = SourceButtons.Handler?.PlatformView as Android.Views.ViewGroup;
    var recents = RecentsView.Handler?.PlatformView as Android.Views.ViewGroup;
    if (recents == null)
      return false;

    if (e.KeyCode == Android.Views.Keycode.DpadDown &&
        sourceGroup != null && IsInside(focused, sourceGroup))
    {
      var first = FirstFocusable(recents);
      if (first != null)
        first.RequestFocus();
      return true;
    }

    if (e.KeyCode == Android.Views.Keycode.DpadUp && IsInside(focused, recents))
    {
      var item = focused;
      while (item.Parent is Android.Views.ViewGroup parent && parent != recents)
        item = parent;

      int index = recents.IndexOfChild(item);
      if (index >= 0 && index < 4 && sourceGroup != null)
      {
        FocusSource(sourceGroup, 2);
        return true;
      }
    }

    if (e.KeyCode == Android.Views.Keycode.DpadUp &&
        sourceGroup != null && IsInside(focused, sourceGroup))
    {
      int index = IndexOfContaining(sourceGroup, focused);
      if (index > 0)
      {
        FocusSource(sourceGroup, index - 1);
        return true;
      }
    }

    return false;
  }

  private static void FocusSource(Android.Views.ViewGroup group, int index)
  {
    var child = group.GetChildAt(index);
    if (child == null)
      return;
    if (child.Focusable)
      child.RequestFocus();
    else if (child is Android.Views.ViewGroup nested)
      FirstFocusable(nested)?.RequestFocus();
  }

  private static int IndexOfContaining(Android.Views.ViewGroup group, Android.Views.View view)
  {
    for (int i = 0; i < group.ChildCount; i++)
    {
      var child = group.GetChildAt(i);
      if (child != null && (child == view || IsInside(view, child as Android.Views.ViewGroup)))
        return i;
    }
    return -1;
  }

  private static bool IsInside(Android.Views.View view, Android.Views.ViewGroup root)
  {
    Android.Views.View? current = view;
    while (current != null)
    {
      if (current == root)
        return true;
      current = current.Parent as Android.Views.View;
    }
    return false;
  }

  private static Android.Views.View? FirstFocusable(Android.Views.ViewGroup group)
  {
    for (int i = 0; i < group.ChildCount; i++)
    {
      var child = group.GetChildAt(i);
      if (child == null)
        continue;
      if (child.Focusable)
        return child;
      if (child is Android.Views.ViewGroup nested)
      {
        var found = FirstFocusable(nested);
        if (found != null)
          return found;
      }
    }
    return null;
  }
#endif

  private void OnGalleryTapped(object? sender, EventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Gallery));

  private void OnFilesTapped(object? sender, EventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Files));

  private void OnCameraTapped(object? sender, EventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Camera));

  private void OnAddTapped(object? sender, EventArgs e)
  {
    if (_selectionOrder.Count == 0)
      return;

    var picked = _selectionOrder.Select(t => t.Source).ToList();
    _ = DismissAsync(new AttachmentRecentsChosen(picked));
  }

  private async void OnRecentClicked(object? sender, EventArgs e)
  {
    if (_closing || sender is not Button button || button.CommandParameter is not RecentThumb thumb)
      return;

    if (button.Parent is VisualElement tile)
      _ = AnimateTapBounceAsync(tile);

    if (_selectionOrder.Remove(thumb))
      thumb.SelectionOrder = 0;
    else
      _selectionOrder.Add(thumb);

    for (int i = 0; i < _selectionOrder.Count; i++)
      _selectionOrder[i].SelectionOrder = i + 1;

    await UpdateActionButtonAsync();
  }

  private static async Task AnimateTapBounceAsync(VisualElement tile)
  {
    await tile.ScaleTo(0.9, 70, Easing.CubicOut);
    await tile.ScaleTo(1, 90, Easing.CubicOut);
  }

  private async Task UpdateActionButtonAsync()
  {
    int count = _selectionOrder.Count;
    ActionCountLabel.Text = count.ToString();

    if (count > 0 && !ActionButton.IsVisible)
    {
      ActionButton.IsVisible = true;
      await Task.WhenAll(
          ActionButton.FadeTo(1, 160, Easing.CubicOut),
          ActionButton.ScaleTo(1, 160, Easing.SpringOut));
    }
    else if (count == 0 && ActionButton.IsVisible)
    {
      await Task.WhenAll(
          ActionButton.FadeTo(0, 140, Easing.CubicIn),
          ActionButton.ScaleTo(0.6, 140, Easing.CubicIn));
      ActionButton.IsVisible = false;
    }
    else if (count > 0)
    {
      _ = AnimateTapBounceAsync(ActionCountBadge);
    }
  }

  private async Task LoadRecentsAsync(CancellationToken ct)
  {
    if (!_recentMedia.IsSupported)
      return;

    try
    {
      if (!await _recentMedia.EnsurePermissionAsync())
        return;

      var images = await _recentMedia.GetRecentImagesAsync(RecentsLimit, ct);
      if (images.Count == 0 || ct.IsCancellationRequested)
        return;

      LoadingIndicator.IsRunning = true;
      LoadingIndicator.IsVisible = true;

      var loaded = await LoadAllThumbnailsAsync(images, ct);
      if (ct.IsCancellationRequested)
        return;

      foreach (var thumb in loaded)
        _recents.Add(thumb);

      await Task.Delay(100);
      foreach (var thumb in loaded)
        thumb.ReloadThumb();
      RefreshRecentsView();
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
      LoadingIndicator.IsRunning = false;
      LoadingIndicator.IsVisible = false;
    }
  }

  private void RefreshRecentsView()
  {
#if ANDROID
    if (RecentsView.Handler?.PlatformView is Android.Views.View view)
    {
      view.RequestLayout();
      view.Invalidate();
    }
#endif
    RecentsView.InvalidateMeasure();
  }

  private async Task<List<RecentThumb>> LoadAllThumbnailsAsync(IReadOnlyList<RecentImage> images, CancellationToken ct)
  {
    var results = new RecentThumb?[images.Count];
    var gate = new SemaphoreSlim(ThumbConcurrency);
    try
    {
      var tasks = images.Select((image, index) => LoadOneThumbnailAsync(image, index, results, gate, ct));
      await Task.WhenAll(tasks);
    }
    finally
    {
      gate.Dispose();
    }

    return results.Where(r => r != null).Select(r => r!).ToList();
  }

  private async Task LoadOneThumbnailAsync(RecentImage image, int index, RecentThumb?[] results, SemaphoreSlim gate, CancellationToken ct)
  {
    try
    {
      await gate.WaitAsync(ct);
      try
      {
        var thumb = new RecentThumb(image);
        using var stream = await _recentMedia.LoadThumbnailAsync(image, ThumbPx, ct);
        if (stream != null)
        {
          using var buffer = new MemoryStream();
          await stream.CopyToAsync(buffer, ct);
          byte[] bytes = buffer.ToArray();
          string dir = Path.Combine(AppPaths.CacheRoot, "thumbs");
          Directory.CreateDirectory(dir);
          string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(image.Id)));
          string path = Path.Combine(dir, key + ".jpg");
          await File.WriteAllBytesAsync(path, bytes, ct);
          thumb.ThumbPath = path;
        }
        results[index] = thumb;
      }
      finally
      {
        gate.Release();
      }
    }
    catch (OperationCanceledException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
  }

  internal sealed class RecentThumb : INotifyPropertyChanged
  {
    private ImageSource? _thumb;
    private int _selectionOrder;

    public RecentThumb(RecentImage source) => Source = source;

    public RecentImage Source { get; }

    public string? ThumbPath { get; set; }

    public ImageSource? Thumb
    {
      get => _thumb;
      set => Set(ref _thumb, value);
    }

    public void ReloadThumb()
    {
      if (string.IsNullOrEmpty(ThumbPath))
        return;

      string path = ThumbPath;
      Thumb = null;
      Thumb = ImageSource.FromFile(path);
    }

    public int SelectionOrder
    {
      get => _selectionOrder;
      set
      {
        if (_selectionOrder == value)
          return;
        _selectionOrder = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(OrderLabel));
        OnPropertyChanged(nameof(OverlayOpacity));
      }
    }

    public bool IsSelected => _selectionOrder > 0;
    public string OrderLabel => _selectionOrder > 0 ? _selectionOrder.ToString() : string.Empty;
    public double OverlayOpacity => _selectionOrder > 0 ? 0.35 : 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
      if (EqualityComparer<T>.Default.Equals(field, value))
        return;
      field = value;
      OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
}
#if ANDROID
internal sealed class SheetKeyListener : Java.Lang.Object, Android.Views.View.IOnKeyListener
{
  private readonly Func<Android.Views.View, Android.Views.Keycode, Android.Views.KeyEvent, bool> _handler;

  public SheetKeyListener(Func<Android.Views.View, Android.Views.Keycode, Android.Views.KeyEvent, bool> handler)
  {
    _handler = handler;
  }

  public bool OnKey(Android.Views.View? v, Android.Views.Keycode keyCode, Android.Views.KeyEvent? e)
    => v != null && e != null && _handler(v, keyCode, e);
}
#endif
