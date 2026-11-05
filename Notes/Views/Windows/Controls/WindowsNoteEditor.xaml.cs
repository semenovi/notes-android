using Notes.Helpers;
using Notes.Models;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using Notes.Views.Pages;
using System.Reactive.Linq;
using System.Timers;

namespace Notes.Views.Windows.Controls;

public partial class WindowsNoteEditor : ContentView
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private readonly MarkdownProcessor _markdownProcessor;
  private readonly Services.ProgressNotificationService _progressService;
  private readonly Services.ToastService _toastService;

  private Note? _currentNote;
  private readonly System.Timers.Timer _autoSaveTimer;
  private bool _hasUnsavedChanges;
  private bool _isEditMode;

  public WindowsNoteEditor()
  {
    InitializeComponent();
    _noteManager = App.Current!.Handler!.MauiContext!.Services.GetService<NoteManager>()!;
    _mediaManager = App.Current!.Handler!.MauiContext!.Services.GetService<MediaManager>()!;
    _markdownProcessor = App.Current!.Handler!.MauiContext!.Services.GetService<MarkdownProcessor>()!;
    _progressService = App.Current!.Handler!.MauiContext!.Services.GetService<Services.ProgressNotificationService>()!;
    _toastService = App.Current!.Handler!.MauiContext!.Services.GetService<Services.ToastService>()!;

    _autoSaveTimer = new System.Timers.Timer(3000) { AutoReset = false };
    _autoSaveTimer.Elapsed += OnAutoSave;

#if WINDOWS
    ContentPreview.Navigating += OnImageViewerNavigating;
    ContentEditor.HandlerChanged += OnContentEditorHandlerChanged;
#endif
  }

#if WINDOWS
  private void OnImageViewerNavigating(object? sender, WebNavigatingEventArgs e)
  {
    if (TaskListMarkdown.TryParseToggleUrl(e.Url, out int taskIndex, out bool isChecked))
    {
      e.Cancel = true;
      _ = ToggleTaskAsync(taskIndex, isChecked);
      return;
    }

    if (!e.Url.StartsWith("img-viewer://open/")) return;
    e.Cancel = true;
    var payload = Uri.UnescapeDataString(e.Url["img-viewer://open/".Length..]);
    _ = OpenImageViewerAsync(payload);
  }

  private async Task OpenImageViewerAsync(string payload)
  {
    string? imageUrl = null;
    string? mediaId = null;

    if (payload.StartsWith("media-"))
    {
      mediaId = payload[6..]; 
      int colonIdx = mediaId.IndexOf(':');
      if (colonIdx > 0 && int.TryParse(mediaId[(colonIdx + 1)..], out int pageIndex))
      {
        mediaId = mediaId[..colonIdx];
        imageUrl = await _markdownProcessor.GetFullResPdfPageDataUriAsync(mediaId, pageIndex);
      }
      else
      {
        imageUrl = await _markdownProcessor.GetFullResDataUriAsync(mediaId);
        if (string.IsNullOrEmpty(imageUrl))
          _markdownProcessor.TryGetCachedDataUri(mediaId, out imageUrl);
      }
    }
    else if (payload.StartsWith("http://") || payload.StartsWith("https://"))
    {
      imageUrl = payload;
    }

    if (string.IsNullOrEmpty(imageUrl)) return;

    var page   = new Notes.Views.Windows.ImageViewerPage(imageUrl, _mediaManager, mediaId);
    var window = new Window(page) { Title = string.Empty };

    EventHandler? onActivated = null;
    onActivated = (s, ev) =>
    {
      window.Activated -= onActivated;
      Notes.Views.Windows.ImageViewerPage.ConfigureWindow(window);
    };
    window.Activated += onActivated;

    Application.Current!.OpenWindow(window);
  }

  private void OnContentEditorHandlerChanged(object? sender, EventArgs e)
  {
    if (ContentEditor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
    {
      textBox.Paste -= OnNativePaste;
      textBox.Paste += OnNativePaste;
    }
  }

  private void OnNativePaste(object sender, Microsoft.UI.Xaml.Controls.TextControlPasteEventArgs e)
  {
    try
    {
      var content = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();

      if (content.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
      if (!content.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap) &&
          !content.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;

      e.Handled = true;
      _ = PasteImagesFromClipboardAsync(showAlertIfEmpty: false);
    }
    catch
    {

    }
  }

  private async Task ToggleTaskAsync(int taskIndex, bool isChecked)
  {
    if (_currentNote == null || _isEditMode) return;

    var updated = TaskListMarkdown.Toggle(_currentNote.Content ?? "", taskIndex, isChecked);
    if (updated == null) return;
    _currentNote.Content = updated;
    await SaveNoteAsync();
  }

#endif

  private void OnAutoSave(object? sender, ElapsedEventArgs e)
  {

    MainThread.BeginInvokeOnMainThread(async () =>
    {
      if (_isEditMode && _currentNote != null && _hasUnsavedChanges)
      {
        await SaveNoteAsync();
        EditDateLabel.Text = FormatDate(_currentNote.Modified);
      }
    });
  }

  public async Task LoadNoteAsync(Note note)
  {
    if (_currentNote != null && _hasUnsavedChanges)
      await SaveNoteAsync();

    _currentNote = note;
    _hasUnsavedChanges = false;
    _isEditMode = false;

    EmptyStateLabel.IsVisible = false;
    EditMode.IsVisible = false;
    ViewMode.IsVisible = true;

    using var session = _progressService.Begin("loading note");
    await UpdatePreviewAsync(session);
  }

  public void ClearEditor()
  {
    _autoSaveTimer.Stop();
    _currentNote = null;
    _hasUnsavedChanges = false;
    _isEditMode = false;
    ViewMode.IsVisible = false;
    EditMode.IsVisible = false;
    EmptyStateLabel.IsVisible = true;
  }

  private async void OnEditClicked(object sender, EventArgs e)
  {
    if (_currentNote == null) return;

    int scrollLine = await EditorScrollSync.GetVisibleSourceLineAsync(ContentPreview);

    _isEditMode = true;

    TitleEntry.Text = _currentNote.Title;
    ContentEditor.Text = _currentNote.Content ?? "";
    EditDateLabel.Text = ViewDateLabel.Text;

    ViewMode.IsVisible = false;
    EditMode.IsVisible = true;

    ApplyEditorScrollLine(scrollLine, _currentNote.Content ?? "");
  }

#if WINDOWS
  private void ApplyEditorScrollLine(int line, string content)
  {
    GetAttachedTextBoxOnce()
        .SelectMany(textBox => GetScrollViewerOnce(textBox).Select(sv => (textBox, sv)))
        .Subscribe(t =>
        {
          t.textBox.DispatcherQueue.TryEnqueue(() =>
          {
            int caret = EditorScrollSync.GetCharOffsetForLine(content, line);
            t.textBox.SelectionStart = caret;
            t.textBox.SelectionLength = 0;
            t.textBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

            int totalLines = EditorScrollSync.GetLineCount(content);
            double target = Math.Min(t.sv.ScrollableHeight,
                (double)line / Math.Max(1, totalLines) * t.sv.ExtentHeight);
            t.sv.ChangeView(null, target, null, true);
            System.Diagnostics.Debug.WriteLine(
                $"[ScrollSync] applied line={line} caret={caret} target={target:F1} " +
                $"extentHeight={t.sv.ExtentHeight:F1} verticalOffsetAfterCall={t.sv.VerticalOffset:F1}");
          });
        });
  }

  private IObservable<Microsoft.UI.Xaml.Controls.TextBox> GetAttachedTextBoxOnce()
  {
    if (ContentEditor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox existing)
      return Observable.Return(existing);

    return Observable.FromEventPattern<EventHandler, EventArgs>(
            h => ContentEditor.HandlerChanged += h,
            h => ContentEditor.HandlerChanged -= h)
        .Select(_ => ContentEditor.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox)
        .Where(tb => tb != null)
        .Take(1)!;
  }

  private static IObservable<Microsoft.UI.Xaml.Controls.ScrollViewer> GetScrollViewerOnce(
      Microsoft.UI.Xaml.Controls.TextBox textBox)
  {
    var immediate = FindScrollViewer(textBox);
    if (immediate != null)
      return Observable.Return(immediate);

    return Observable.FromEventPattern<EventHandler<object>, object>(
            h => textBox.LayoutUpdated += h,
            h => textBox.LayoutUpdated -= h)
        .Select(_ => FindScrollViewer(textBox))
        .Where(sv => sv != null)
        .Take(1)!;
  }

  private int CaptureEditorScrollLine()
  {
    if (ContentEditor.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBox textBox)
      return 0;

    var scrollViewer = FindScrollViewer(textBox);
    if (scrollViewer == null || scrollViewer.ExtentHeight <= 0)
      return 0;

    int totalLines = EditorScrollSync.GetLineCount(ContentEditor.Text);
    int line = (int)Math.Round(scrollViewer.VerticalOffset / scrollViewer.ExtentHeight * totalLines);
    System.Diagnostics.Debug.WriteLine(
        $"[ScrollSync] capture verticalOffset={scrollViewer.VerticalOffset:F1} " +
        $"extentHeight={scrollViewer.ExtentHeight:F1} line={line}");
    return line;
  }

  private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(Microsoft.UI.Xaml.DependencyObject root)
  {
    int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
    for (int i = 0; i < count; i++)
    {
      var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
      if (child is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
        return scrollViewer;

      var nested = FindScrollViewer(child);
      if (nested != null)
        return nested;
    }
    return null;
  }
#else
  private void ApplyEditorScrollLine(int line, string content) { }
  private int CaptureEditorScrollLine() => 0;
#endif

  private async void OnSaveClicked(object sender, EventArgs e)
  {
    _autoSaveTimer.Stop();

    int scrollLine = CaptureEditorScrollLine();

    await SaveNoteAsync();
    await UpdatePreviewAsync();
    await EditorScrollSync.ScrollToSourceLineAsync(ContentPreview, scrollLine);

    _isEditMode = false;
    EditMode.IsVisible = false;
    ViewMode.IsVisible = true;
  }

  private async Task SaveNoteAsync()
  {
    if (_currentNote == null) return;
    try
    {
      _currentNote.Modified = DateTime.Now;
      await _noteManager.UpdateNoteAsync(_currentNote);
      _hasUnsavedChanges = false;
    }
    catch (Exception ex)
    {
      _toastService.Show($"failed to save: {ex.Message}");
    }
  }

  private async Task UpdatePreviewAsync(Services.ProgressSession? session = null)
  {
    if (_currentNote == null) return;

    TitleViewLabel.Text = _currentNote.Title;
    ViewDateLabel.Text = FormatDate(_currentNote.Modified);

    string content = _currentNote.Content ?? "";
    try
    {
      string body = string.IsNullOrWhiteSpace(content)
          ? "<p style='color:#8E8E93'>No content</p>"
          : await _markdownProcessor.ConvertToHtmlAsync(content);

      string tempPath = Path.Combine(AppPaths.CacheRoot, "note_preview.html");
      await File.WriteAllTextAsync(tempPath, WrapHtml(body), System.Text.Encoding.UTF8);

      var navTask = WaitForNavigationAsync(ContentPreview);
      ContentPreview.Source = new UrlWebViewSource { Url = "file:///" + tempPath.Replace('\\', '/') };
      await navTask;

      if (!string.IsNullOrWhiteSpace(content))
        await _markdownProcessor.InjectImagesIntoWebViewAsync(content, ContentPreview,
            (loaded, total) => session?.Report((double)loaded / total,
                total - loaded > 0 ? $"{total - loaded} images left" : null));
    }
    catch
    {
      var fallback = System.Net.WebUtility.HtmlEncode(content);
      ContentPreview.Source = new HtmlWebViewSource
      {
        Html = WrapHtml($"<pre style='white-space:pre-wrap'>{fallback}</pre>")
      };
    }
  }

  private static Task WaitForNavigationAsync(WebView webView)
  {
    var tcs = new TaskCompletionSource<bool>();
    EventHandler<WebNavigatedEventArgs>? handler = null;
    handler = (s, e) => { webView.Navigated -= handler; tcs.TrySetResult(true); };
    webView.Navigated += handler;
    return tcs.Task;
  }

  private static string WrapHtml(string body) => $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  body {{ font-family: -apple-system, 'Segoe UI', system-ui, sans-serif; font-size: 15px; color: #1C1C1E; padding: 16px 24px; line-height: 1.65; margin: 0; background: white; overflow-wrap: break-word; word-break: break-word; }}
  h1 {{ font-size: 20px; font-weight: 700; margin: 0 0 8px; }}
  h2 {{ font-size: 17px; font-weight: 600; margin: 16px 0 6px; }}
  h3 {{ font-size: 15px; font-weight: 600; margin: 12px 0 4px; }}
  p {{ margin: 0 0 8px; }}
  code {{ background: #F2F2F7; padding: 2px 5px; border-radius: 4px; font-family: 'Consolas', monospace; font-size: 13px; }}
  pre {{ background: #F2F2F7; padding: 12px; border-radius: 8px; }}
  pre code {{ background: none; padding: 0; }}
  blockquote {{ border-left: 3px solid #C6C6C8; margin: 0 0 8px; padding: 0 0 0 14px; color: #636366; }}
  ul, ol {{ padding-left: 22px; margin: 0 0 8px; }}
  a {{ color: #007AFF; text-decoration: none; }}
  img {{ max-width: 100%; border-radius: 4px; }}
  .media-lazy {{ display: block; min-height: 80px; background: linear-gradient(90deg,#f0f0f0 25%,#e8e8e8 50%,#f0f0f0 75%); background-size: 200% 100%; animation: shimmer 1.5s infinite; border-radius: 4px; }}
  @keyframes shimmer {{ 0%{{background-position:200% 0}} 100%{{background-position:-200% 0}} }}
  .pdf-page {{ display: block; margin-bottom: 8px; }}
  hr {{ border: none; border-top: 1px solid #E5E5EA; margin: 16px 0; }}
  {ImageViewerHtml.ViewerCss}
  {ImageViewerHtml.CopyCodeCss}
  {TaskListMarkdown.Css}
</style>
</head>
<body>{ImageViewerHtml.ViewerDiv}{body}{ImageViewerHtml.ViewerScript}{ImageViewerHtml.CopyCodeScript}{TaskListMarkdown.Script}</body>
</html>";

  private static string FormatDate(DateTime dt) =>
      dt.ToString("d MMMM yyyy, HH:mm");

  private void OnTitleChanged(object sender, TextChangedEventArgs e)
  {
    if (_currentNote == null || _currentNote.Title == e.NewTextValue) return;
    _currentNote.Title = e.NewTextValue ?? "";
    _hasUnsavedChanges = true;
    StartAutoSave();
  }

  private void OnContentChanged(object sender, TextChangedEventArgs e)
  {
    var normalized = (e.NewTextValue ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
    if (_currentNote == null || _currentNote.Content == normalized) return;
    _currentNote.Content = normalized;
    _hasUnsavedChanges = true;
    StartAutoSave();
  }

  private void StartAutoSave()
  {
    _autoSaveTimer.Stop();
    _autoSaveTimer.Start();
  }

  private void OnBoldClicked(object sender, EventArgs e) =>
      InsertMarkdownFormat("**", "**", "text");

  private void OnItalicClicked(object sender, EventArgs e) =>
      InsertMarkdownFormat("*", "*", "text");

  private void OnListClicked(object sender, EventArgs e) =>
      InsertText("\n- ");

  private void OnTaskListClicked(object sender, EventArgs e) =>
      InsertText("\n- [ ] ");

  private void OnHeaderClicked(object sender, EventArgs e) =>
      InsertText("\n## ");

  private async void OnAddImageClicked(object sender, EventArgs e)
  {
    try
    {
      var results = await FilePicker.PickMultipleAsync(new PickOptions
      {
        FileTypes = MediaFilePickerTypes.ImagesAndPdf,
        PickerTitle = "select images or pdf"
      });
      if (results == null || !results.Any()) return;

      var fileList = results.ToList();
      int total = fileList.Count;
      using var session = _progressService.Begin("adding images", delayMs: 0);

      var parts = new List<string>();
      for (int i = 0; i < fileList.Count; i++)
      {
        session.Report((double)i / total, total > 1 ? $"{i + 1} of {total}" : null);
        var result = fileList[i];
        using var stream = await result.OpenReadAsync();
        var media = await _mediaManager.AddMediaAsync(stream, result.FileName);
        parts.Add($"![{result.FileName}]({_mediaManager.GetMediaUrl(media.Id)})");
        session.Report((double)(i + 1) / total, total > 1 ? $"{i + 1} of {total}" : null);
      }

      InsertText(string.Join("\n\n", parts));
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
    }
  }

  private async void OnPasteImageClicked(object sender, EventArgs e)
  {
    await PasteImagesFromClipboardAsync(showAlertIfEmpty: true);
  }

  private async Task PasteImagesFromClipboardAsync(bool showAlertIfEmpty)
  {
    try
    {
      var images = await ClipboardImageHelper.GetImagesAsync();
      if (images.Count == 0)
      {
        if (showAlertIfEmpty)
          _toastService.Show("no image in the clipboard");
        return;
      }

      int total = images.Count;
      using var session = _progressService.Begin("adding images", delayMs: 0);

      var parts = new List<string>();
      for (int i = 0; i < images.Count; i++)
      {
        session.Report((double)i / total, total > 1 ? $"{i + 1} of {total}" : null);
        using var stream = images[i].Stream;
        var media = await _mediaManager.AddMediaAsync(stream, images[i].FileName);
        parts.Add($"![{images[i].FileName}]({_mediaManager.GetMediaUrl(media.Id)})");
        session.Report((double)(i + 1) / total, total > 1 ? $"{i + 1} of {total}" : null);
      }

      InsertText(string.Join("\n\n", parts));
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
    }
  }

  private void InsertMarkdownFormat(string prefix, string suffix, string placeholder)
  {
    var content = ContentEditor.Text ?? "";
    int pos = ContentEditor.CursorPosition;
    int selLen = ContentEditor.SelectionLength;

    string insert;
    if (selLen > 0)
    {
      var selected = content.Substring(pos, selLen);
      insert = $"{prefix}{selected}{suffix}";
      ContentEditor.Text = content.Remove(pos, selLen).Insert(pos, insert);
    }
    else
    {
      insert = $"{prefix}{placeholder}{suffix}";
      ContentEditor.Text = content.Insert(pos, insert);
    }
    ContentEditor.CursorPosition = pos + insert.Length;
    UpdateCurrentNoteContent();
  }

  private void InsertText(string text)
  {
    var content = ContentEditor.Text ?? "";
    int pos = ContentEditor.CursorPosition;
    ContentEditor.Text = content.Insert(pos, text);
    ContentEditor.CursorPosition = pos + text.Length;
    UpdateCurrentNoteContent();
  }

  private void UpdateCurrentNoteContent()
  {
    if (_currentNote == null) return;
    _currentNote.Content = ContentEditor.Text;
    _hasUnsavedChanges = true;
    StartAutoSave();
  }

  protected override void OnParentChanged()
  {
    base.OnParentChanged();
    if (Parent == null)
    {
      _autoSaveTimer.Stop();
      _autoSaveTimer.Dispose();
    }
  }
}
