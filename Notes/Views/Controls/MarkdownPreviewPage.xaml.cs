namespace Notes.Views.Pages;

public partial class MarkdownPreviewPage : ContentPage
{
  public MarkdownPreviewPage(string htmlContent)
  {
    InitializeComponent();

    string fullHtml = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1'>
                    <style>
                        body {{ 
                            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                            padding: 15px;
                            line-height: 1.5;
                        }}
                        img {{ max-width: 100%; }}
                        pre {{ background-color: #f5f5f5; padding: 10px; overflow-x: auto; }}
                        code {{ background-color: #f5f5f5; padding: 2px 4px; }}
                    </style>
                </head>
                <body>
                    {htmlContent}
                </body>
                </html>";

    PreviewWebView.Source = new HtmlWebViewSource { Html = fullHtml };
  }

  private async void OnCloseClicked(object sender, EventArgs e)
  {
    await Navigation.PopModalAsync();
  }
}