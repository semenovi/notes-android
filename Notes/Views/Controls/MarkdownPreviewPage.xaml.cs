using Notes.Helpers;

namespace Notes.Views.Pages;

public partial class MarkdownPreviewPage : ContentPage
{
  private readonly Services.Markdown.MarkdownProcessor _markdownProcessor;

  public MarkdownPreviewPage(string markdown)
  {
    InitializeComponent();
    _markdownProcessor = App.Current.Handler.MauiContext.Services.GetService<Services.Markdown.MarkdownProcessor>();
    _ = LoadPreviewAsync(markdown);
  }

  private async Task LoadPreviewAsync(string markdown)
  {
    string html = await _markdownProcessor.ConvertToHtmlAsync(markdown);
    string fullHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; padding: 15px; line-height: 1.5; }}
        img {{ max-width: 100%; border-radius: 4px; }}
        pre {{ background-color: #f5f5f5; padding: 10px; overflow-x: auto; }}
        code {{ background-color: #f5f5f5; padding: 2px 4px; }}
        .media-lazy {{ display: block; min-height: 80px; background: linear-gradient(90deg,#f0f0f0 25%,#e8e8e8 50%,#f0f0f0 75%); background-size: 200% 100%; animation: shimmer 1.5s infinite; border-radius: 4px; }}
        @keyframes shimmer {{ 0%{{background-position:200% 0}} 100%{{background-position:-200% 0}} }}
        {ImageViewerHtml.ViewerCss}
    </style>
</head>
<body>{ImageViewerHtml.ViewerDiv}{html}{ImageViewerHtml.ViewerScript}</body>
</html>";

    var tcs = new TaskCompletionSource<bool>();
    EventHandler<WebNavigatedEventArgs>? handler = null;
    handler = (s, e) => { PreviewWebView.Navigated -= handler; tcs.TrySetResult(true); };
    PreviewWebView.Navigated += handler;
    PreviewWebView.Source = new HtmlWebViewSource { Html = fullHtml };
    await tcs.Task;

    await _markdownProcessor.InjectImagesIntoWebViewAsync(markdown, PreviewWebView);
  }

  private async void OnCloseClicked(object sender, EventArgs e)
  {
    await Navigation.PopModalAsync();
  }
}