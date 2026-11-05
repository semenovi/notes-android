using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

  public AttachmentSheet(IRecentMediaProvider recentMedia)
  {
    InitializeComponent();
    _recentMedia = recentMedia;
    RecentsView.ItemsSource = _recents;
  }

  public async Task<AttachmentSheetResult> ShowAsync()
  {
    _recents.Clear();
    _selectionOrder.Clear();
    _closing = false;
    _lifetime = new CancellationTokenSource();
    _completion = new TaskCompletionSource<AttachmentSheetResult>();

    IsVisible = true;
    await AnimateInAsync();
    _ = LoadRecentsAsync(_lifetime.Token);

    var result = await _completion.Task;
    IsVisible = false;
    return result;
  }

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

  private void OnGalleryTapped(object? sender, TappedEventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Gallery));

  private void OnFilesTapped(object? sender, TappedEventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Files));

  private void OnCameraTapped(object? sender, TappedEventArgs e)
      => _ = DismissAsync(new AttachmentSourceChosen(AttachmentSource.Camera));

  private void OnAddTapped(object? sender, EventArgs e)
  {
    if (_selectionOrder.Count == 0)
      return;

    var picked = _selectionOrder.Select(t => t.Source).ToList();
    _ = DismissAsync(new AttachmentRecentsChosen(picked));
  }

  private async void OnRecentTapped(object? sender, TappedEventArgs e)
  {
    if (_closing || sender is not VisualElement tile || tile.BindingContext is not RecentThumb thumb)
      return;

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
          thumb.Thumb = ImageSource.FromStream(() => new MemoryStream(bytes));
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

    public ImageSource? Thumb
    {
      get => _thumb;
      set => Set(ref _thumb, value);
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
