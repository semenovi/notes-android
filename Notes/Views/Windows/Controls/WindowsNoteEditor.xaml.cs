using Notes.Helpers;
using Notes.Models;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using Notes.Views.Pages;
using System.Timers;

namespace Notes.Views.Windows.Controls;

public partial class WindowsNoteEditor : ContentView
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private readonly MarkdownProcessor _markdownProcessor;

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

    _autoSaveTimer = new System.Timers.Timer(3000) { AutoReset = false };
    _autoSaveTimer.Elapsed += OnAutoSave;

#if WINDOWS
    ContentPreview.Navigating += OnImageViewerNavigating;
#endif
  }

#if WINDOWS
  private void OnImageViewerNavigating(object? sender, WebNavigatingEventArgs e)
  {
    // Payload is encoded directly in the URL: img-viewer://open/{encodeURIComponent(id|src)}
    if (!e.Url.StartsWith("img-viewer://open/")) return;
    e.Cancel = true;
    var payload = Uri.UnescapeDataString(e.Url["img-viewer://open/".Length..]);
    _ = OpenImageViewerAsync(payload);
  }

  private async Task OpenImageViewerAsync(string payload)
  {
    string? imageUrl = null;

    if (payload.StartsWith("media-"))
    {
      var mediaId = payload[6..]; // strip "media-"
      _markdownProcessor.TryGetCachedDataUri(mediaId, out imageUrl);
    }
    else if (payload.StartsWith("http://") || payload.StartsWith("https://"))
    {
      imageUrl = payload;
    }

    if (string.IsNullOrEmpty(imageUrl)) return;

    var page   = new Notes.Views.Windows.ImageViewerPage(imageUrl);
    var window = new Window(page) { Title = string.Empty };

    // Window.Activated fires after the WinUI handler is fully initialised —
    // the only reliable moment to call AppWindow APIs on a newly opened window.
    EventHandler? onActivated = null;
    onActivated = (s, ev) =>
    {
      window.Activated -= onActivated;
      Notes.Views.Windows.ImageViewerPage.ConfigureWindow(window);
    };
    window.Activated += onActivated;

    Application.Current!.OpenWindow(window);
  }

#endif

  private void OnAutoSave(object? sender, ElapsedEventArgs e)
  {
    // Timer fires on thread pool — must dispatch to UI thread
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

    await UpdatePreviewAsync();
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

  private void OnEditClicked(object sender, EventArgs e)
  {
    if (_currentNote == null) return;
    _isEditMode = true;

    TitleEntry.Text = _currentNote.Title;
    ContentEditor.Text = _currentNote.Content ?? "";
    EditDateLabel.Text = ViewDateLabel.Text;

    ViewMode.IsVisible = false;
    EditMode.IsVisible = true;

    ContentEditor.Focus();
  }

  private async void OnSaveClicked(object sender, EventArgs e)
  {
    _autoSaveTimer.Stop();
    await SaveNoteAsync();
    await UpdatePreviewAsync();

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
      await Application.Current!.Windows[0].Page!.DisplayAlert("Error",
          $"Failed to save: {ex.Message}", "OK");
    }
  }

  private async Task UpdatePreviewAsync()
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

      string tempPath = Path.Combine(FileSystem.CacheDirectory, "note_preview.html");
      await File.WriteAllTextAsync(tempPath, WrapHtml(body), System.Text.Encoding.UTF8);

      var navTask = WaitForNavigationAsync(ContentPreview);
      ContentPreview.Source = new UrlWebViewSource { Url = "file:///" + tempPath.Replace('\\', '/') };
      await navTask;

      if (!string.IsNullOrWhiteSpace(content))
        await _markdownProcessor.InjectImagesIntoWebViewAsync(content, ContentPreview);
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
  hr {{ border: none; border-top: 1px solid #E5E5EA; margin: 16px 0; }}
  {ImageViewerHtml.ViewerCss}
  {ImageViewerHtml.CopyCodeCss}
</style>
</head>
<body>{ImageViewerHtml.ViewerDiv}{body}{ImageViewerHtml.ViewerScript}{ImageViewerHtml.CopyCodeScript}</body>
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

  private void OnHeaderClicked(object sender, EventArgs e) =>
      InsertText("\n## ");

  private async void OnAddImageClicked(object sender, EventArgs e)
  {
    try
    {
      var results = await FilePicker.PickMultipleAsync(new PickOptions
      {
        FileTypes = FilePickerFileType.Images,
        PickerTitle = "Select images"
      });
      if (results == null || !results.Any()) return;

      var parts = new List<string>();
      foreach (var result in results)
      {
        using var stream = await result.OpenReadAsync();
        var media = await _mediaManager.AddMediaAsync(stream, result.FileName);
        parts.Add($"![{result.FileName}]({_mediaManager.GetMediaUrl(media.Id)})");
      }

      InsertText(string.Join("\n\n", parts));
    }
    catch (Exception ex)
    {
      await Application.Current!.Windows[0].Page!.DisplayAlert("Error", ex.Message, "OK");
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
