using Notes.Models;
using Notes.Services;
using Notes.Services.Notes;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notes.Views.Pages;

[QueryProperty(nameof(NoteId), "NoteId")]
public partial class NoteEditorPage : ContentPage, INotifyPropertyChanged
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private readonly ProgressNotificationService _progressService;

  private Note _note;
  private string _noteId;
  private string _content;

  public string NoteId
  {
    get => _noteId;
    set
    {
      _noteId = value;
      LoadNoteAsync().ConfigureAwait(false);
    }
  }

  public string Title
  {
    get => _note?.Title ?? "Editor";
  }

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

  public NoteEditorPage(NoteManager noteManager, MediaManager mediaManager,
      ProgressNotificationService progressService)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _mediaManager = mediaManager;
    _progressService = progressService;
    BindingContext = this;
  }

  protected override void OnAppearing()
  {
    base.OnAppearing();
    _progressService.ShowRequested += PageProgress.ShowProgress;
    _progressService.UpdateRequested += PageProgress.UpdateProgress;
    _progressService.HideRequested += PageProgress.HideProgress;
    if (_progressService.Current != null)
      PageProgress.ShowProgress(_progressService.Current);
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    _progressService.ShowRequested -= PageProgress.ShowProgress;
    _progressService.UpdateRequested -= PageProgress.UpdateProgress;
    _progressService.HideRequested -= PageProgress.HideProgress;
    PageProgress.Reset();
  }

  private async Task LoadNoteAsync()
  {
    if (string.IsNullOrEmpty(NoteId))
      return;

    using var session = _progressService.Begin("Loading note");
    _note = await _noteManager.GetNoteAsync(NoteId);
    if (_note != null)
    {
      Content = _note.Content;
      OnPropertyChanged(nameof(Title));
    }
  }

  private async void OnSaveClicked(object sender, EventArgs e)
  {
    if (_note == null)
      return;

    _note.Content = Content;
    _note.Modified = DateTime.Now;
    await _noteManager.UpdateNoteAsync(_note);

    await DisplayAlert("Success", "Note saved successfully", "OK");
  }

  private async void OnPreviewClicked(object sender, EventArgs e)
  {
    if (string.IsNullOrEmpty(Content))
      return;

    var page = new MarkdownPreviewPage(Content);
    await Navigation.PushModalAsync(page);
  }

  private async void OnAddMediaClicked(object sender, EventArgs e)
  {
    var fileResults = await FilePicker.PickMultipleAsync(new PickOptions
    {
      FileTypes = FilePickerFileType.Images,
      PickerTitle = "Select images"
    });

    if (fileResults == null || !fileResults.Any())
      return;

    var fileList = fileResults.ToList();
    int total = fileList.Count;
    using var session = _progressService.Begin("Adding images", delayMs: 0);

    var parts = new List<string>();
    for (int i = 0; i < fileList.Count; i++)
    {
      session.Report((double)i / total, total > 1 ? $"{i + 1} of {total}" : null);
      var fileResult = fileList[i];
      using var stream = await fileResult.OpenReadAsync();
      var mediaItem = await _mediaManager.AddMediaAsync(stream, fileResult.FileName);
      parts.Add($"![{fileResult.FileName}]({_mediaManager.GetMediaUrl(mediaItem.Id)})");
      session.Report((double)(i + 1) / total, total > 1 ? $"{i + 1} of {total}" : null);
    }

    int cursorPosition = ContentEditor.CursorPosition;
    string insertText = string.Join("\n\n", parts);

    Content = Content.Insert(cursorPosition, insertText);
    ContentEditor.CursorPosition = cursorPosition + insertText.Length;
  }

  private void OnFormatBoldClicked(object sender, EventArgs e)
  {
    InsertMarkdownFormat("**", "**", "bold text");
  }

  private void OnFormatItalicClicked(object sender, EventArgs e)
  {
    InsertMarkdownFormat("*", "*", "italic text");
  }

  private void OnFormatListClicked(object sender, EventArgs e)
  {
    int cursorPosition = ContentEditor.CursorPosition;
    string insertText = "\n- List item\n- Another item\n- One more item\n";

    string newContent = Content.Insert(cursorPosition, insertText);
    Content = newContent;

    ContentEditor.CursorPosition = cursorPosition + insertText.Length;
  }

  private void InsertMarkdownFormat(string prefix, string suffix, string placeholder)
  {
    int cursorPosition = ContentEditor.CursorPosition;
    string selectedText = string.Empty;

    if (ContentEditor.SelectionLength > 0)
    {
      int selectionStart = ContentEditor.CursorPosition;
      selectedText = Content.Substring(selectionStart, ContentEditor.SelectionLength);
    }

    string insertText = string.IsNullOrEmpty(selectedText) ?
        $"{prefix}{placeholder}{suffix}" :
        $"{prefix}{selectedText}{suffix}";

    string newContent;
    if (ContentEditor.SelectionLength > 0)
    {
      int selectionStart = ContentEditor.CursorPosition;
      newContent = Content.Remove(selectionStart, ContentEditor.SelectionLength)
          .Insert(selectionStart, insertText);
    }
    else
    {
      newContent = Content.Insert(cursorPosition, insertText);
    }

    Content = newContent;

    if (string.IsNullOrEmpty(selectedText))
    {
      ContentEditor.CursorPosition = cursorPosition + prefix.Length;
    }
    else
    {
      ContentEditor.CursorPosition = cursorPosition + insertText.Length;
    }
  }

  public new event PropertyChangedEventHandler PropertyChanged;

  protected override void OnPropertyChanged([CallerMemberName] string propertyName = "")
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}