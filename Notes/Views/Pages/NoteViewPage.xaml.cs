using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Markdown;
using Notes.Services.Notes;

namespace Notes.Views.Pages;

[QueryProperty(nameof(NoteId), "NoteId")]
public partial class NoteViewPage : ContentPage
{
  private readonly NoteManager _noteManager;
  private readonly MarkdownProcessor _markdownProcessor;
  private readonly ProgressNotificationService _progressService;
  private Note _note;
  private string _noteId;
  private bool _isSwipingBack;
#if ANDROID
  private Android.Views.View? _prevPageView;
  private Android.Views.ViewGroup? _actualCurrentContainer;
  private Android.Views.View? _nativeShadow;
  private float _shadowWidthPx;
  private float _density = 1f;
#endif

  public string NoteId
  {
    get => _noteId;
    set
    {
      _noteId = value;
      LoadNoteAsync().ConfigureAwait(false);
    }
  }

  public string Title => _note?.Title ?? "Note";

  public NoteViewPage(NoteManager noteManager, MarkdownProcessor markdownProcessor,
      ProgressNotificationService progressService)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _markdownProcessor = markdownProcessor;
    _progressService = progressService;
    BindingContext = this;
    NoteContentWebView.Navigating += OnWebViewNavigating;
  }

  private async Task LoadNoteAsync()
  {
    if (string.IsNullOrEmpty(NoteId))
      return;

    _note = await _noteManager.GetNoteAsync(NoteId);
    if (_note != null)
    {
      OnPropertyChanged(nameof(Title));
      await RenderNoteContentAsync();
    }
  }

  private async Task RenderNoteContentAsync()
  {
    if (_note == null)
      return;

    string content = _note.Content ?? "";
    string html = await _markdownProcessor.ConvertToHtmlAsync(content);

    var navTask = WaitForNavigationAsync(NoteContentWebView);
    NoteContentWebView.Source = new HtmlWebViewSource { Html = BuildFullHtml(html) };
    await navTask;

    using var session = _progressService.Begin("Loading note");
    await _markdownProcessor.InjectImagesIntoWebViewAsync(content, NoteContentWebView,
        (loaded, total) => session.Report((double)loaded / total,
            total - loaded > 0 ? $"{total - loaded} images left" : null));
  }

  private static Task WaitForNavigationAsync(WebView webView)
  {
    var tcs = new TaskCompletionSource<bool>();
    EventHandler<WebNavigatedEventArgs>? handler = null;
    handler = (s, e) => { webView.Navigated -= handler; tcs.TrySetResult(true); };
    webView.Navigated += handler;
    return tcs.Task;
  }

  private static string BuildFullHtml(string body) => $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; padding: 15px; line-height: 1.5; margin-top: 15px; overflow-wrap: break-word; word-break: break-word; }}
        img {{ max-width: 100%; border-radius: 4px; }}
        pre {{ background-color: #f5f5f5; padding: 10px; }}
        code {{ background-color: #f5f5f5; padding: 2px 4px; }}
        .media-lazy {{ display: block; min-height: 80px; background: linear-gradient(90deg,#f0f0f0 25%,#e8e8e8 50%,#f0f0f0 75%); background-size: 200% 100%; animation: shimmer 1.5s infinite; border-radius: 4px; }}
        @keyframes shimmer {{ 0%{{background-position:200% 0}} 100%{{background-position:-200% 0}} }}
        {ImageViewerHtml.ViewerCss}
        {ImageViewerHtml.CopyCodeCss}
    </style>
</head>
<body>{ImageViewerHtml.ViewerDiv}{body}{ImageViewerHtml.ViewerScript}{ImageViewerHtml.CopyCodeScript}</body>
</html>";

  private void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
  {
#if ANDROID
    if (e.Url == "swipe://disable")
    {
      e.Cancel = true;
      global::Notes.Platforms.Android.SwipeBackGesture.OnProgress = null;
      global::Notes.Platforms.Android.SwipeBackGesture.OnEnd = null;
      global::Notes.Platforms.Android.SwipeBackGesture.OnCancel = null;
    }
    else if (e.Url == "swipe://enable")
    {
      e.Cancel = true;
      global::Notes.Platforms.Android.SwipeBackGesture.OnProgress = OnSwipeProgress;
      global::Notes.Platforms.Android.SwipeBackGesture.OnEnd = OnSwipeEnd;
      global::Notes.Platforms.Android.SwipeBackGesture.OnCancel = () => _ = SpringBackAsync();
    }
#endif
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

  private async void OnEditClicked(object sender, EventArgs e)
  {
    await NavigateToEditor();
  }

  private async void OnDeleteNoteClicked(object sender, EventArgs e)
  {
    bool confirm = await DisplayAlert("Confirm Delete", $"Are you sure you want to delete note '{Title}'?", "Yes", "No");
    if (confirm)
    {
      await _noteManager.DeleteNoteAsync(_note.Id);
      await Shell.Current.GoToAsync("..");
    }
  }

  private async Task NavigateToEditor()
  {
    if (_note == null)
      return;

    var navigationParameter = new Dictionary<string, object>
        {
            { "NoteId", _note.Id }
        };

    await Shell.Current.GoToAsync(nameof(NoteEditorPage), navigationParameter);
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
    _progressService.ShowRequested += PageProgress.ShowProgress;
    _progressService.UpdateRequested += PageProgress.UpdateProgress;
    _progressService.HideRequested += PageProgress.HideProgress;
    if (_progressService.Current != null)
      PageProgress.ShowProgress(_progressService.Current);

    if (_note != null)
      RenderNoteContentAsync().ConfigureAwait(false);
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
#if ANDROID
    if (!_isSwipingBack)
      HidePreviousPage();
    Notes.Platforms.Android.SwipeBackGesture.OnProgress = null;
    Notes.Platforms.Android.SwipeBackGesture.OnEnd = null;
    Notes.Platforms.Android.SwipeBackGesture.OnCancel = null;
#endif
    _progressService.ShowRequested -= PageProgress.ShowProgress;
    _progressService.UpdateRequested -= PageProgress.UpdateProgress;
    _progressService.HideRequested -= PageProgress.HideProgress;
    PageProgress.Reset();
  }
}