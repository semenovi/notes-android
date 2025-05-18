using Notes.Models;
using Notes.Services.Markdown;
using Notes.Services.Notes;

namespace Notes.Views.Pages;

[QueryProperty(nameof(NoteId), "NoteId")]
public partial class NoteViewPage : ContentPage
{
  private readonly NoteManager _noteManager;
  private readonly MarkdownProcessor _markdownProcessor;
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

  public NoteViewPage(NoteManager noteManager, MarkdownProcessor markdownProcessor)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _markdownProcessor = markdownProcessor;
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

    string html = await _markdownProcessor.ConvertToHtmlAsync(_note.Content);
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
                        margin-top: 15px;
                    }}
                    img {{ max-width: 100%; }}
                    pre {{ background-color: #f5f5f5; padding: 10px; overflow-x: auto; }}
                    code {{ background-color: #f5f5f5; padding: 2px 4px; }}
                </style>
            </head>
            <body>
                {html}
            </body>
            </html>";

    NoteContentWebView.Source = new HtmlWebViewSource { Html = fullHtml };
  }

  private async void OnEditClicked(object sender, EventArgs e)
  {
    await NavigateToEditor();
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

    if (_note != null)
    {
      RenderNoteContentAsync().ConfigureAwait(false);
    }
  }
}