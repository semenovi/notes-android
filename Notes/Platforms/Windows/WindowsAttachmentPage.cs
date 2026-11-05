using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Notes.Helpers;
using Notes.Services.Media;
using SkiaSharp;

namespace Notes.Platforms.Windows;

public sealed class WindowsAttachmentPage : ContentPage
{
  private readonly TaskCompletionSource<IReadOnlyList<PendingAttachment>?> _completion = new();
  private readonly List<Draft> _drafts = new();
  private readonly Image _preview = new() { Aspect = Aspect.AspectFit };
  private readonly Label _emptyLabel = new()
  {
    Text = "add photos, files or paste from clipboard",
    TextColor = Color.FromArgb("#8E8E93"),
    FontSize = 15,
    HorizontalOptions = LayoutOptions.Center,
    VerticalOptions = LayoutOptions.Center,
  };
  private readonly Label _fileNameLabel = new()
  {
    FontSize = 12,
    TextColor = Color.FromArgb("#8E8E93"),
    LineBreakMode = LineBreakMode.TailTruncation,
    VerticalOptions = LayoutOptions.Center,
  };
  private readonly HorizontalStackLayout _strip = new() { Spacing = 8, Padding = new Thickness(10, 6) };
  private readonly Grid _previewHost = new();
  private readonly AbsoluteLayout _cropLayer = new() { IsVisible = false };
  private readonly BoxView _maskTop = new() { Color = Color.FromArgb("#99000000"), InputTransparent = true };
  private readonly BoxView _maskBottom = new() { Color = Color.FromArgb("#99000000"), InputTransparent = true };
  private readonly BoxView _maskLeft = new() { Color = Color.FromArgb("#99000000"), InputTransparent = true };
  private readonly BoxView _maskRight = new() { Color = Color.FromArgb("#99000000"), InputTransparent = true };
  private readonly Border _cropBorder = new()
  {
    Stroke = new SolidColorBrush(Colors.White),
    StrokeThickness = 2,
    BackgroundColor = Colors.Transparent,
    StrokeShape = new RoundRectangle { CornerRadius = 0 },
  };
  private readonly Border _handleTopLeft = CreateHandle();
  private readonly Border _handleTopRight = CreateHandle();
  private readonly Border _handleBottomLeft = CreateHandle();
  private readonly Border _handleBottomRight = CreateHandle();
  private readonly Button _cropButton;
  private readonly Button _rotateLeftButton;
  private readonly Button _rotateRightButton;
  private readonly Button _resetButton;
  private readonly Button _removeButton;

  private int _selectedIndex = -1;
  private Rect _imageRect;
  private Rect _cropRect;
  private Rect _panStartRect;
  private int _activeCorner;
  private bool _cropMode;

  private WindowsAttachmentPage()
  {
    Title = "attach";
    BackgroundColor = Color.FromArgb("#F7F7FA");

    var addPhotosButton = CreateTextButton("add photos");
    addPhotosButton.Clicked += async (_, _) => await AddImagesAsync();
    var addFilesButton = CreateTextButton("add files");
    addFilesButton.Clicked += async (_, _) => await AddFilesAsync();
    var pasteButton = CreateTextButton("paste");
    pasteButton.Clicked += async (_, _) => await PasteAsync();
    var closeButton = CreateTextButton("close");
    closeButton.Clicked += (_, _) => Finish(null);

    var header = new Grid
    {
      Padding = new Thickness(16, 10),
      ColumnDefinitions =
      {
        new ColumnDefinition(GridLength.Star),
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Auto),
      },
      ColumnSpacing = 8,
    };
    header.Add(new Label
    {
      Text = "attach images",
      FontSize = 18,
      FontAttributes = FontAttributes.Bold,
      VerticalOptions = LayoutOptions.Center,
    }, 0, 0);
    header.Add(addPhotosButton, 1, 0);
    header.Add(addFilesButton, 2, 0);
    header.Add(pasteButton, 3, 0);
    header.Add(closeButton, 4, 0);

    _previewHost.BackgroundColor = Color.FromArgb("#1C1C1E");
    _previewHost.Add(_preview);
    _previewHost.Add(_emptyLabel);
    _cropLayer.Add(_maskTop);
    _cropLayer.Add(_maskBottom);
    _cropLayer.Add(_maskLeft);
    _cropLayer.Add(_maskRight);
    _cropLayer.Add(_cropBorder);
    _cropLayer.Add(_handleTopLeft);
    _cropLayer.Add(_handleTopRight);
    _cropLayer.Add(_handleBottomLeft);
    _cropLayer.Add(_handleBottomRight);
    _previewHost.Add(_cropLayer);

    _cropBorder.GestureRecognizers.Add(CreatePan(0));
    _handleTopLeft.GestureRecognizers.Add(CreatePan(1));
    _handleTopRight.GestureRecognizers.Add(CreatePan(2));
    _handleBottomLeft.GestureRecognizers.Add(CreatePan(3));
    _handleBottomRight.GestureRecognizers.Add(CreatePan(4));

    var stripScroll = new ScrollView
    {
      Orientation = ScrollOrientation.Horizontal,
      HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
      HeightRequest = 92,
      BackgroundColor = Colors.White,
      Content = _strip,
    };

    _rotateLeftButton = CreateTextButton("rotate left");
    _rotateLeftButton.Clicked += (_, _) => Rotate(-90);
    _rotateRightButton = CreateTextButton("rotate right");
    _rotateRightButton.Clicked += (_, _) => Rotate(90);
    _cropButton = CreateTextButton("crop");
    _cropButton.Clicked += (_, _) => OnCropButtonClicked();
    _resetButton = CreateTextButton("reset");
    _resetButton.Clicked += (_, _) => ResetSelected();
    _removeButton = CreateTextButton("remove");
    _removeButton.Clicked += (_, _) => RemoveSelected();

    var tools = new HorizontalStackLayout
    {
      Spacing = 8,
      Padding = new Thickness(16, 8),
      Children = { _rotateLeftButton, _rotateRightButton, _cropButton, _resetButton, _removeButton },
    };

    var cancelButton = CreateTextButton("cancel");
    cancelButton.Clicked += (_, _) => Finish(null);
    var attachButton = new Button
    {
      Text = "attach",
      BackgroundColor = Color.FromArgb("#007AFF"),
      TextColor = Colors.White,
      CornerRadius = 6,
      Padding = new Thickness(18, 8),
      FontAttributes = FontAttributes.Bold,
    };
    attachButton.Clicked += (_, _) => Finish(BuildResult());

    var footer = new Grid
    {
      Padding = new Thickness(16, 8),
      ColumnDefinitions =
      {
        new ColumnDefinition(GridLength.Star),
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Auto),
      },
      ColumnSpacing = 10,
    };
    footer.Add(_fileNameLabel, 0, 0);
    footer.Add(cancelButton, 1, 0);
    footer.Add(attachButton, 2, 0);

    var root = new Grid
    {
      RowDefinitions =
      {
        new RowDefinition(GridLength.Auto),
        new RowDefinition(GridLength.Star),
        new RowDefinition(GridLength.Auto),
        new RowDefinition(GridLength.Auto),
        new RowDefinition(GridLength.Auto),
      },
    };
    root.Add(header, 0, 0);
    root.Add(_previewHost, 0, 1);
    root.Add(stripScroll, 0, 2);
    root.Add(tools, 0, 3);
    root.Add(footer, 0, 4);
    Content = root;

    UpdateSelection();
  }

  public static async Task<IReadOnlyList<PendingAttachment>?> ShowAsync(
      IReadOnlyList<(string Name, byte[] Data)>? initial = null)
  {
    var page = new WindowsAttachmentPage();
    if (initial is { Count: > 0 })
    {
      foreach (var item in initial)
        page.AddBytes(item.Name, item.Data);
      page.UpdateSelection();
    }
    var window = new Window(page)
    {
      Title = "attach images",
      Width = 980,
      Height = 720,
      MinimumWidth = 640,
      MinimumHeight = 520,
    };
    Application.Current!.OpenWindow(window);
    var result = await page._completion.Task;
    return result;
  }

  protected override void OnHandlerChanged()
  {
    base.OnHandlerChanged();
    if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element)
      return;
    element.AllowDrop = true;
    element.DragOver += OnNativeDragOver;
    element.Drop += OnNativeDrop;
  }

  private static void OnNativeDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
  {
    if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
      return;
    e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    e.DragUIOverride.Caption = "add images";
  }

  private async void OnNativeDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
  {
    if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
      return;

    var items = await e.DataView.GetStorageItemsAsync();
    foreach (var item in items.OfType<global::Windows.Storage.StorageFile>())
    {
      string name = item.Name;
      string extension = System.IO.Path.GetExtension(name).ToLowerInvariant();
      if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".pdf"))
        continue;

      using var randomAccess = await item.OpenReadAsync();
      using var stream = randomAccess.AsStreamForRead();
      using var buffer = new MemoryStream();
      await stream.CopyToAsync(buffer);
      AddBytes(name, buffer.ToArray());
    }

    UpdateSelection();
  }

  protected override void OnDisappearing()
  {
    base.OnDisappearing();
    if (!_completion.Task.IsCompleted)
      _completion.TrySetResult(null);
  }

  private static Button CreateTextButton(string text) => new()
  {
    Text = text,
    BackgroundColor = Colors.Transparent,
    TextColor = Color.FromArgb("#007AFF"),
    BorderWidth = 0,
    FontSize = 13,
    Padding = new Thickness(8, 4),
    MinimumHeightRequest = 34,
  };

  private static Border CreateHandle() => new()
  {
    WidthRequest = 18,
    HeightRequest = 18,
    BackgroundColor = Colors.White,
    Stroke = new SolidColorBrush(Color.FromArgb("#1C1C1E")),
    StrokeThickness = 1,
    StrokeShape = new RoundRectangle { CornerRadius = 3 },
  };

  private PanGestureRecognizer CreatePan(int corner)
  {
    var pan = new PanGestureRecognizer();
    pan.PanUpdated += (_, e) => OnCropPan(e, corner);
    return pan;
  }

  private async Task AddImagesAsync()
  {
    var files = await FilePicker.PickMultipleAsync(new PickOptions
    {
      FileTypes = MediaFilePickerTypes.ImagesOnly,
      PickerTitle = "select images",
    });
    if (files == null)
      return;
    foreach (var file in files)
      await AddFileAsync(file);
    UpdateSelection();
  }

  private async Task AddFilesAsync()
  {
    var files = await FilePicker.PickMultipleAsync(new PickOptions
    {
      FileTypes = MediaFilePickerTypes.ImagesAndPdf,
      PickerTitle = "select images or pdf",
    });
    if (files == null)
      return;
    foreach (var file in files)
      await AddFileAsync(file);
    UpdateSelection();
  }

  private async Task AddFileAsync(FileResult file)
  {
    byte[] data;
    using (var stream = await file.OpenReadAsync())
    using (var buffer = new MemoryStream())
    {
      await stream.CopyToAsync(buffer);
      data = buffer.ToArray();
    }

    AddBytes(file.FileName, data);
  }

  private void AddBytes(string fileName, byte[] data)
  {
    bool isPdf = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    if (isPdf)
    {
      _drafts.Add(new Draft(fileName, data, AttachmentKind.Pdf));
      if (_selectedIndex < 0)
        _selectedIndex = 0;
      return;
    }

    var draft = CreateImageDraft(fileName, data);
    if (draft == null)
      return;
    _drafts.Add(draft);
    if (_selectedIndex < 0)
      _selectedIndex = _drafts.Count - 1;
  }

  private async Task PasteAsync()
  {
    var images = await ClipboardImageHelper.GetImagesAsync();
    foreach (var image in images)
    {
      using (image.Stream)
      {
        using var buffer = new MemoryStream();
        await image.Stream.CopyToAsync(buffer);
        var draft = CreateImageDraft(image.FileName, buffer.ToArray());
        if (draft == null)
          continue;
        _drafts.Add(draft);
        if (_selectedIndex < 0)
          _selectedIndex = _drafts.Count - 1;
      }
    }
    UpdateSelection();
  }

  private Draft? CreateImageDraft(string fileName, byte[] data)
  {
    var normalized = NormalizeImage(data);
    if (normalized == null)
      return null;
    return new Draft(fileName, normalized.Value.Data, AttachmentKind.Image)
    {
      PixelWidth = normalized.Value.Width,
      PixelHeight = normalized.Value.Height,
      OriginalData = normalized.Value.Data,
      OriginalWidth = normalized.Value.Width,
      OriginalHeight = normalized.Value.Height,
    };
  }

  private void UpdateSelection()
  {
    if (_drafts.Count == 0)
      _selectedIndex = -1;
    else if (_selectedIndex < 0 || _selectedIndex >= _drafts.Count)
      _selectedIndex = 0;

    var draft = SelectedDraft;
    _preview.IsVisible = draft is { Kind: AttachmentKind.Image };
    _emptyLabel.IsVisible = draft == null || draft.Kind == AttachmentKind.Pdf;
    _emptyLabel.Text = draft switch
    {
      null => _drafts.Count == 0 ? "add photos, files or paste from clipboard" : "select an image",
      { Kind: AttachmentKind.Pdf } => "PDF document",
      _ => "select an image",
    };
    _preview.Source = draft?.Preview;
    _fileNameLabel.Text = draft == null
      ? ""
      : draft.Kind == AttachmentKind.Pdf
        ? draft.FileName
        : $"{draft.FileName}  ·  {draft.PixelWidth} × {draft.PixelHeight}";

    bool image = draft is { Kind: AttachmentKind.Image };
    _rotateLeftButton.IsEnabled = image;
    _rotateRightButton.IsEnabled = image;
    _cropButton.IsEnabled = image;
    _resetButton.IsEnabled = image;
    _removeButton.IsEnabled = draft != null;

    if (!image)
      ExitCrop();

    RebuildStrip();
  }

  private void RebuildStrip()
  {
    _strip.Children.Clear();
    for (int i = 0; i < _drafts.Count; i++)
    {
      int index = i;
      var draft = _drafts[i];
      var border = new Border
      {
        WidthRequest = 72,
        HeightRequest = 72,
        StrokeThickness = 2,
        Stroke = new SolidColorBrush(i == _selectedIndex ? Color.FromArgb("#007AFF") : Colors.Transparent),
        BackgroundColor = Color.FromArgb("#E5E5EA"),
        StrokeShape = new RoundRectangle { CornerRadius = 6 },
        Content = draft.Kind == AttachmentKind.Image
          ? new Image { Source = draft.Preview, Aspect = Aspect.AspectFill }
          : new Label
          {
            Text = "PDF",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#8E8E93"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
          },
      };
      var tap = new TapGestureRecognizer();
      tap.Tapped += (_, _) =>
      {
        _selectedIndex = index;
        ExitCrop();
        UpdateSelection();
      };
      border.GestureRecognizers.Add(tap);
      _strip.Children.Add(border);
    }

    var add = new Border
    {
      WidthRequest = 72,
      HeightRequest = 72,
      StrokeThickness = 1,
      Stroke = new SolidColorBrush(Color.FromArgb("#C7C7CC")),
      StrokeShape = new RoundRectangle { CornerRadius = 6 },
      Content = new Label
      {
        Text = "+",
        FontSize = 28,
        TextColor = Color.FromArgb("#007AFF"),
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
      },
    };
    var addTap = new TapGestureRecognizer();
    addTap.Tapped += async (_, _) => await AddImagesAsync();
    add.GestureRecognizers.Add(addTap);
    _strip.Children.Add(add);
  }

  private Draft? SelectedDraft =>
      _selectedIndex >= 0 && _selectedIndex < _drafts.Count ? _drafts[_selectedIndex] : null;

  private void Rotate(int degrees)
  {
    var draft = SelectedDraft;
    if (draft == null || draft.Kind != AttachmentKind.Image)
      return;
    var bitmap = SKBitmap.Decode(draft.Data);
    if (bitmap == null)
      return;
    var rotated = RotateBitmap(bitmap, degrees);
    bitmap.Dispose();
    draft.SetImage(EncodePng(rotated), rotated.Width, rotated.Height);
    rotated.Dispose();
    ExitCrop();
    UpdateSelection();
  }

  private void ResetSelected()
  {
    var draft = SelectedDraft;
    if (draft == null || draft.OriginalData == null)
      return;
    draft.SetImage(draft.OriginalData, draft.OriginalWidth, draft.OriginalHeight);
    ExitCrop();
    UpdateSelection();
  }

  private void RemoveSelected()
  {
    if (_selectedIndex < 0 || _selectedIndex >= _drafts.Count)
      return;
    _drafts.RemoveAt(_selectedIndex);
    if (_selectedIndex >= _drafts.Count)
      _selectedIndex = _drafts.Count - 1;
    ExitCrop();
    UpdateSelection();
  }

  private void ToggleCrop()
  {
    if (_cropMode)
    {
      ExitCrop();
      return;
    }

    var draft = SelectedDraft;
    if (draft == null || draft.Kind != AttachmentKind.Image)
      return;

    _imageRect = GetImageRect(draft);
    if (_imageRect.Width < 40 || _imageRect.Height < 40)
      return;

    _cropMode = true;
    _cropLayer.IsVisible = true;
    _cropRect = InsetRect(_imageRect, 0.08);
    UpdateCropVisuals();
    _cropButton.Text = "apply crop";
  }

  private void ExitCrop()
  {
    if (!_cropMode)
      return;
    _cropMode = false;
    _cropLayer.IsVisible = false;
    _cropButton.Text = "crop";
  }

  private void OnCropPan(PanUpdatedEventArgs e, int corner)
  {
    if (!_cropMode)
      return;

    if (e.StatusType == GestureStatus.Started)
    {
      _panStartRect = _cropRect;
      _activeCorner = corner;
      return;
    }

    if (e.StatusType != GestureStatus.Running && e.StatusType != GestureStatus.Completed)
      return;

    if (e.StatusType == GestureStatus.Completed)
      return;

    double dx = e.TotalX;
    double dy = e.TotalY;
    var left = _panStartRect.Left;
    var top = _panStartRect.Top;
    var right = _panStartRect.Right;
    var bottom = _panStartRect.Bottom;

    if (corner == 0)
    {
      left += dx;
      right += dx;
      top += dy;
      bottom += dy;
      if (left < _imageRect.Left)
      {
        right += _imageRect.Left - left;
        left = _imageRect.Left;
      }
      if (right > _imageRect.Right)
      {
        left -= right - _imageRect.Right;
        right = _imageRect.Right;
      }
      if (top < _imageRect.Top)
      {
        bottom += _imageRect.Top - top;
        top = _imageRect.Top;
      }
      if (bottom > _imageRect.Bottom)
      {
        top -= bottom - _imageRect.Bottom;
        bottom = _imageRect.Bottom;
      }
    }
    else
    {
      if (corner is 1 or 3)
        left = Math.Min(Math.Max(_imageRect.Left, left + dx), right - 40);
      else
        right = Math.Max(Math.Min(_imageRect.Right, right + dx), left + 40);
      if (corner is 1 or 2)
        top = Math.Min(Math.Max(_imageRect.Top, top + dy), bottom - 40);
      else
        bottom = Math.Max(Math.Min(_imageRect.Bottom, bottom + dy), top + 40);
    }

    _cropRect = new Rect(left, top, Math.Max(40, right - left), Math.Max(40, bottom - top));
    UpdateCropVisuals();
  }

  private void UpdateCropVisuals()
  {
    SetBounds(_maskTop, new Rect(0, 0, _previewHost.Width, _cropRect.Top));
    SetBounds(_maskBottom, new Rect(0, _cropRect.Bottom, _previewHost.Width, Math.Max(0, _previewHost.Height - _cropRect.Bottom)));
    SetBounds(_maskLeft, new Rect(0, _cropRect.Top, _cropRect.Left, _cropRect.Height));
    SetBounds(_maskRight, new Rect(_cropRect.Right, _cropRect.Top, Math.Max(0, _previewHost.Width - _cropRect.Right), _cropRect.Height));
    SetBounds(_cropBorder, _cropRect);
    SetBounds(_handleTopLeft, new Rect(_cropRect.Left - 9, _cropRect.Top - 9, 18, 18));
    SetBounds(_handleTopRight, new Rect(_cropRect.Right - 9, _cropRect.Top - 9, 18, 18));
    SetBounds(_handleBottomLeft, new Rect(_cropRect.Left - 9, _cropRect.Bottom - 9, 18, 18));
    SetBounds(_handleBottomRight, new Rect(_cropRect.Right - 9, _cropRect.Bottom - 9, 18, 18));
  }

  private static void SetBounds(View view, Rect rect)
  {
    AbsoluteLayout.SetLayoutBounds(view, rect);
    AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
  }

  private Rect GetImageRect(Draft draft)
  {
    double hostWidth = _previewHost.Width;
    double hostHeight = _previewHost.Height;
    if (hostWidth <= 0 || hostHeight <= 0 || draft.PixelWidth <= 0 || draft.PixelHeight <= 0)
      return Rect.Zero;

    double scale = Math.Min(hostWidth / draft.PixelWidth, hostHeight / draft.PixelHeight);
    double width = draft.PixelWidth * scale;
    double height = draft.PixelHeight * scale;
    return new Rect((hostWidth - width) / 2, (hostHeight - height) / 2, width, height);
  }

  private static Rect InsetRect(Rect rect, double fraction)
  {
    double dx = rect.Width * fraction;
    double dy = rect.Height * fraction;
    return new Rect(rect.Left + dx, rect.Top + dy, rect.Width - dx * 2, rect.Height - dy * 2);
  }

  private void ApplyCrop()
  {
    var draft = SelectedDraft;
    if (draft == null || draft.Kind != AttachmentKind.Image || _imageRect.Width <= 0)
      return;

    double scaleX = draft.PixelWidth / _imageRect.Width;
    double scaleY = draft.PixelHeight / _imageRect.Height;
    int sx = (int)Math.Round((_cropRect.Left - _imageRect.Left) * scaleX);
    int sy = (int)Math.Round((_cropRect.Top - _imageRect.Top) * scaleY);
    int sw = (int)Math.Round(_cropRect.Width * scaleX);
    int sh = (int)Math.Round(_cropRect.Height * scaleY);
    sx = Math.Clamp(sx, 0, draft.PixelWidth - 1);
    sy = Math.Clamp(sy, 0, draft.PixelHeight - 1);
    sw = Math.Clamp(sw, 1, draft.PixelWidth - sx);
    sh = Math.Clamp(sh, 1, draft.PixelHeight - sy);

    var bitmap = SKBitmap.Decode(draft.Data);
    if (bitmap == null)
      return;
    var cropped = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(cropped))
    {
      canvas.DrawBitmap(bitmap, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(0, 0, sw, sh));
    }
    bitmap.Dispose();
    draft.SetImage(EncodePng(cropped), sw, sh);
    cropped.Dispose();
    ExitCrop();
    UpdateSelection();
  }

  private void Finish(IReadOnlyList<PendingAttachment>? result)
  {
    if (_completion.Task.IsCompleted)
      return;
    _completion.TrySetResult(result);
    if (Window != null)
      Application.Current!.CloseWindow(Window);
  }

  private IReadOnlyList<PendingAttachment>? BuildResult()
  {
    if (_drafts.Count == 0)
      return null;

    var result = new List<PendingAttachment>(_drafts.Count);
    foreach (var draft in _drafts)
    {
      var data = draft.Data;
      string name = draft.Kind == AttachmentKind.Image
        ? System.IO.Path.GetFileNameWithoutExtension(draft.FileName) + ".png"
        : draft.FileName;
      result.Add(new PendingAttachment
      {
        FileName = name,
        Kind = draft.Kind,
        OpenAsync = _ => Task.FromResult<Stream>(new MemoryStream(data)),
      });
    }
    return result;
  }

  private void OnCropButtonClicked()
  {
    if (_cropMode)
      ApplyCrop();
    else
      ToggleCrop();
  }

  private static (byte[] Data, int Width, int Height)? NormalizeImage(byte[] data)
  {
    using var input = new MemoryStream(data);
    using var codec = SKCodec.Create(input);
    if (codec == null)
      return null;
    using var bitmap = SKBitmap.Decode(codec);
    if (bitmap == null)
      return null;
    var oriented = ApplyOrientation(bitmap, codec.EncodedOrigin);
    var encoded = EncodePng(oriented);
    int width = oriented.Width;
    int height = oriented.Height;
    oriented.Dispose();
    return (encoded, width, height);
  }

  private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
  {
    int rotate = origin switch
    {
      SKEncodedOrigin.RightTop => 90,
      SKEncodedOrigin.BottomRight => 180,
      SKEncodedOrigin.LeftBottom => 270,
      _ => 0,
    };
    bool flipX = origin is SKEncodedOrigin.TopRight or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
    bool flipY = origin is SKEncodedOrigin.BottomLeft;
    return Transform(source, rotate, flipX, flipY);
  }

  private static SKBitmap RotateBitmap(SKBitmap source, int degrees) =>
      Transform(source, degrees, false, false);

  private static SKBitmap Transform(SKBitmap source, int degrees, bool flipX, bool flipY)
  {
    int width = Math.Abs(degrees) % 180 == 0 ? source.Width : source.Height;
    int height = Math.Abs(degrees) % 180 == 0 ? source.Height : source.Width;
    var target = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(target);
    canvas.Translate(width / 2f, height / 2f);
    canvas.RotateDegrees(degrees);
    if (flipX)
      canvas.Scale(-1, 1);
    if (flipY)
      canvas.Scale(1, -1);
    canvas.Translate(-source.Width / 2f, -source.Height / 2f);
    canvas.DrawBitmap(source, 0, 0);
    return target;
  }

  private static byte[] EncodePng(SKBitmap bitmap)
  {
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
  }

  private sealed class Draft
  {    public Draft(string fileName, byte[] data, AttachmentKind kind)
    {
      FileName = fileName;
      Data = data;
      Kind = kind;
      if (kind == AttachmentKind.Image)
      {
        using var bitmap = SKBitmap.Decode(data);
        PixelWidth = bitmap?.Width ?? 0;
        PixelHeight = bitmap?.Height ?? 0;
        Preview = ImageSource.FromStream(() => new MemoryStream(data));
      }
    }

    public string FileName { get; }
    public byte[] Data { get; private set; }
    public AttachmentKind Kind { get; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public ImageSource? Preview { get; private set; }
    public byte[]? OriginalData { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }

    public void SetImage(byte[] data, int width, int height)
    {
      Data = data;
      PixelWidth = width;
      PixelHeight = height;
      Preview = ImageSource.FromStream(() => new MemoryStream(data));
    }
  }
}
