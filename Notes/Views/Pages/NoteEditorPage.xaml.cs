using Notes.Helpers;
using Notes.Models;
using Notes.Services;
using Notes.Services.Media;
using Notes.Services.Notes;
using Notes.Views.Controls;
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
  private readonly AttachmentInserter _attachmentInserter;
  private readonly IRecentMediaProvider _recentMediaProvider;
  private readonly AttachmentSheet _attachSheet;

  private Note _note;
  private string _noteId;
  private string _content;
  private int? _pendingScrollLine;

  private double _lastPageHeight;
  private double _lastPageWidth;

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
      ProgressNotificationService progressService, ToastService toastService,
      AttachmentInserter attachmentInserter, IRecentMediaProvider recentMediaProvider)
  {
    InitializeComponent();
    _noteManager = noteManager;
    _mediaManager = mediaManager;
    _progressService = progressService;
    _toastService = toastService;
    _attachmentInserter = attachmentInserter;
    _recentMediaProvider = recentMediaProvider;
    BindingContext = this;
    Notes.Helpers.OverscrollStretch.Enable(EditorScrollView);
    SizeChanged += OnPageSizeChanged;

    _attachSheet = new AttachmentSheet(_recentMediaProvider);
    RootGrid.Add(_attachSheet);
    Grid.SetRow(_attachSheet, 0);
    Grid.SetRowSpan(_attachSheet, 2);
  }

  private void OnPageSizeChanged(object sender, EventArgs e)
  {
    double height = Height;
    double width = Width;
    if (double.IsNaN(height) || height <= 0)
      return;

    double previousHeight = _lastPageHeight;
    double previousWidth = _lastPageWidth;
    _lastPageHeight = height;
    _lastPageWidth = width;

    if (previousHeight <= 0)
      return;

    double delta = previousHeight - height;
    bool widthStable = Math.Abs(previousWidth - width) < 1;
    if (!widthStable || Math.Abs(delta) < 100)
      return; 

    AttachButton.TranslationY = delta;
    AttachButton.TranslateTo(0, 0, 200, Easing.CubicOut);
  }

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

  private async void OnAttachClicked(object sender, EventArgs e)
  {
    if (_note == null)
      return;

    int caret = Math.Clamp(ContentEditor.CursorPosition, 0, (Content ?? string.Empty).Length);
    ContentEditor.Unfocus();

    var result = await _attachSheet.ShowAsync();

    IReadOnlyList<PendingAttachment>? pending = result switch
    {
      AttachmentRecentsChosen recents => recents.Items.Select(ToPendingImage).ToList(),
      AttachmentSourceChosen chosen => await CollectFromSourceAsync(chosen.Source),
      _ => null,
    };

    await InsertAttachmentsAsync(pending, caret);
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

      var pending = images
          .Select(img => new PendingAttachment
          {
            FileName = img.FileName,
            Kind = AttachmentKind.Image,
            OpenAsync = _ => Task.FromResult<Stream>(new MemoryStream(img.Stream.ToArray())),
          })
          .ToList();

      int caret = Math.Clamp(ContentEditor.CursorPosition, 0, (Content ?? string.Empty).Length);
      await InsertAttachmentsAsync(pending, caret);
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
    }
  }

  private async Task InsertAttachmentsAsync(IReadOnlyList<PendingAttachment>? pending, int caret)
  {
    if (pending == null || pending.Count == 0)
      return;

    string insertText;
    try
    {
      insertText = await _attachmentInserter.BuildInsertTextAsync(pending) ?? string.Empty;
    }
    catch (Exception ex)
    {
      _toastService.Show(ex.Message);
      return;
    }

    if (string.IsNullOrEmpty(insertText))
      return;

    string content = Content ?? string.Empty;
    int position = Math.Clamp(caret, 0, content.Length);
    Content = content.Insert(position, insertText);
    ContentEditor.CursorPosition = position + insertText.Length;
  }

  private async Task<IReadOnlyList<PendingAttachment>?> CollectFromSourceAsync(AttachmentSource source)
  {
    switch (source)
    {
      case AttachmentSource.Gallery:
      {
        var picked = await FilePicker.PickMultipleAsync(new PickOptions
        {
          FileTypes = MediaFilePickerTypes.ImagesOnly,
          PickerTitle = "select images",
        });
        return picked?.Select(ToPending).ToList();
      }

      case AttachmentSource.Files:
      {
        var picked = await FilePicker.PickMultipleAsync(new PickOptions
        {
          FileTypes = MediaFilePickerTypes.ImagesAndPdf,
          PickerTitle = "select images or pdf",
        });
        return picked?.Select(ToPending).ToList();
      }

      case AttachmentSource.Camera:
      {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
          _toastService.Show("camera not available");
          return null;
        }

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
          _toastService.Show("camera permission denied");
          return null;
        }

        var photo = await MediaPicker.Default.CapturePhotoAsync();
        if (photo == null)
          return null;

        return new[] { ToPending(photo) };
      }

      default:
        return null;
    }
  }

  private static PendingAttachment ToPending(FileResult file)
  {
    var kind = file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
        ? AttachmentKind.Pdf
        : AttachmentKind.Image;

    return new PendingAttachment
    {
      FileName = file.FileName,
      Kind = kind,
      OpenAsync = _ => file.OpenReadAsync(),
    };
  }

  private PendingAttachment ToPendingImage(RecentImage image) => new()
  {
    FileName = image.DisplayName,
    Kind = AttachmentKind.Image,
    OpenAsync = ct => _recentMediaProvider.OpenAsync(image, ct),
  };

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
