using Notes.Models;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Controls;

public partial class MarkdownEditor : ContentView, INotifyPropertyChanged
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private readonly MarkdownProcessor _markdownProcessor;

  private string _content = string.Empty;
  private Note _currentNote;

  public string Content
  {
    get => _content;
    set
    {
      if (_content != value)
      {
        _content = value;
        OnPropertyChanged();
      }
    }
  }

  public event EventHandler NoteSaved;

  public MarkdownEditor()
  {
    InitializeComponent();
    _noteManager = App.Current.Handler.MauiContext.Services.GetService<NoteManager>();
    _mediaManager = App.Current.Handler.MauiContext.Services.GetService<MediaManager>();
    _markdownProcessor = App.Current.Handler.MauiContext.Services.GetService<MarkdownProcessor>();
    BindingContext = this;
  }

  public void LoadNote(Note note)
  {
    _currentNote = note;
    Content = note.Content;
  }

  private async void OnSaveClicked(object sender, EventArgs e)
  {
    if (_currentNote == null)
      return;

    _currentNote.Content = Content;
    await _noteManager.UpdateNoteAsync(_currentNote);

    NoteSaved?.Invoke(this, EventArgs.Empty);
  }

  private async void OnPreviewClicked(object sender, EventArgs e)
  {
    if (string.IsNullOrEmpty(Content))
      return;

    string html = _markdownProcessor.ConvertToHtml(Content);
    var page = new Views.Pages.MarkdownPreviewPage(html);
    await Application.Current.MainPage.Navigation.PushModalAsync(page);
  }

  private async void OnAddMediaClicked(object sender, EventArgs e)
  {
    var fileResult = await FilePicker.PickAsync(new PickOptions
    {
      FileTypes = FilePickerFileType.Images,
      PickerTitle = "Select an image"
    });

    if (fileResult == null)
      return;

    using (var stream = await fileResult.OpenReadAsync())
    {
      var mediaItem = await _mediaManager.AddMediaAsync(stream, fileResult.FileName);
      string mediaUrl = _mediaManager.GetMediaUrl(mediaItem.Id);

      int cursorPosition = ContentEditor.CursorPosition;
      string insertText = $"![{fileResult.FileName}]({mediaUrl})";

      string newContent = Content.Insert(cursorPosition, insertText);
      Content = newContent;

      ContentEditor.CursorPosition = cursorPosition + insertText.Length;
    }
  }

  public new event PropertyChangedEventHandler PropertyChanged;

  protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}