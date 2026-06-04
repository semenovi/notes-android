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
    _progressService.ShowRequested -= PageProgress.ShowProgress;
    _progressService.UpdateRequested -= PageProgress.UpdateProgress;
    _progressService.HideRequested -= PageProgress.HideProgress;
    PageProgress.Reset();
  }
}