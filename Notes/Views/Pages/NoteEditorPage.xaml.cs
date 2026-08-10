using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Notes;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

namespace Notes.Views.Pages;

[QueryProperty(nameof(NoteId), "NoteId")]
[QueryProperty(nameof(ScrollLine), "ScrollLine")]
public partial class NoteEditorPage : ContentPage, INotifyPropertyChanged
{
  private readonly NoteManager _noteManager;
  private readonly MediaManager _mediaManager;
  private readonly ProgressNotificationService _progressService;
  private readonly ToastService _toastService;

  private Note _note;
  private string _noteId;
  private string _content;
  private int? _pendingScrollLine;

  public string NoteId
  {
    get => _noteId;
    set
    {
      _noteId = value;
      LoadNoteAsync().ConfigureAwait(false);
    }
  }

  public string ScrollLine
  {
    get => _pendingScrollLine?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    set
    {
      if (int.TryParse(value, out int line) && line > 0)
        _pendingScrollLine = line;
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
      ProgressNotificationService progressService, ToastService toastService)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _mediaManager = mediaManager;
    _progressService = progressService;
    _toastService = toastService;
    BindingContext = this;
  }

  // reads the native ScrollView directly (ScrollY / child height in real pixels) instead
  // of MAUI's cross-platform Height mirror, which lags behind the native AutoSize height
  // and doesn't move on plain (no-tap) scrolling. The fraction of the editor's own
  // scrollable range is converted to a source line via its own line count - a raw pixel
  // fraction doesn't transfer to the WebView, whose blocks are laid out at different
  // heights (headings, images) than the editor's uniform monospace lines.
  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    if (_note == null)
      return;

    double fraction = CaptureNativeScrollFraction();
    int totalLines = EditorScrollSync.GetLineCount(Content);
    int line = (int)Math.Round(fraction * totalLines);
    EditorScrollSync.SetReturnLine(_note.Id, line);
  }

  private async Task LoadNoteAsync()
  {
    if (string.IsNullOrEmpty(NoteId))
      return;

    using var session = _progressService.Begin("loading note");
    _note = await _noteManager.GetNoteAsync(NoteId);
    if (_note != null)
    {
      Content = _note.Content;
      OnPropertyChanged(nameof(Title));
      if (_pendingScrollLine is int line)
      {
        _pendingScrollLine = null;
        ContentEditor.CursorPosition = EditorScrollSync.GetCharOffsetForLine(Content, line);
        int totalLines = EditorScrollSync.GetLineCount(Content);
        ApplyNativeScrollFraction((double)line / Math.Max(1, totalLines));
      }
    }
  }

#if ANDROID
  // mirrors the WebView's top alignment instead of EditText's "bring point into view"
  // heuristic, which centers rather than top-aligns
  private double CaptureNativeScrollFraction()
  {
    if (EditorScrollView.Handler?.PlatformView is not Android.Views.ViewGroup native || native.ChildCount == 0)
      return 0;

    int contentHeight = native.GetChildAt(0).Height;
    int scrollable = contentHeight - native.Height;
    return scrollable > 0 ? Math.Clamp((double)native.ScrollY / scrollable, 0, 1) : 0;
  }

  private bool TryApplyNativeScrollFraction(double fraction)
  {
    if (EditorScrollView.Handler?.PlatformView is not Android.Views.ViewGroup native || native.ChildCount == 0)
      return false;

    var content = native.GetChildAt(0);
    if (content.Height <= 0)
      return false;

    // posted rather than called inline: CursorPosition, set right after this returns,
    // can itself queue a native "bring point into view" adjustment on the same message
    // queue; posting puts our correction after it in that FIFO queue so ours sticks
    native.Post(() =>
    {
      int scrollable = Math.Max(0, content.Height - native.Height);
      native.ScrollTo(0, (int)(fraction * scrollable));
    });
    return true;
  }

  private void ApplyNativeScrollFraction(double fraction)
  {
    if (TryApplyNativeScrollFraction(fraction))
      return;

    // not laid out yet - react to the next size change instead of polling
    Observable.FromEventPattern<EventHandler, EventArgs>(
            h => ContentEditor.SizeChanged += h,
            h => ContentEditor.SizeChanged -= h)
        .Select(_ => TryApplyNativeScrollFraction(fraction))
        .Where(applied => applied)
        .Take(1)
        .Subscribe();
  }
#else
  private double CaptureNativeScrollFraction() => 0;

  private void ApplyNativeScrollFraction(double fraction) { }
#endif

  private async void OnSaveClicked(object sender, EventArgs e)
  {
    if (_note == null)
      return;

    _note.Content = Content;
    _note.Modified = DateTime.Now;
    await _noteManager.UpdateNoteAsync(_note);

    _toastService.Show("note saved successfully");
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
      PickerTitle = "select images"
    });

    if (fileResults == null || !fileResults.Any())
      return;

    var fileList = fileResults.ToList();
    int total = fileList.Count;
    using var session = _progressService.Begin("adding images", delayMs: 0);

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

  private async void OnPasteImageClicked(object sender, EventArgs e)
  {
    try
    {
      var images = await ClipboardImageHelper.GetImagesAsync();
      if (images.Count == 0)
      {
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
        var mediaItem = await _mediaManager.AddMediaAsync(stream, images[i].FileName);
        parts.Add($"![{images[i].FileName}]({_mediaManager.GetMediaUrl(mediaItem.Id)})");
        session.Report((double)(i + 1) / total, total > 1 ? $"{i + 1} of {total}" : null);
      }

      string content = Content ?? "";
      int cursorPosition = Math.Clamp(ContentEditor.CursorPosition, 0, content.Length);
      string insertText = string.Join("\n\n", parts);

      Content = content.Insert(cursorPosition, insertText);
      ContentEditor.CursorPosition = cursorPosition + insertText.Length;
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
    }
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
