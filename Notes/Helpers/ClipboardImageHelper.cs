namespace Notes.Helpers;

public static class ClipboardImageHelper
{
  public record ClipboardImage(MemoryStream Stream, string FileName);

  /// <summary>
  /// Reads images from the system clipboard. Returns an empty list when the
  /// clipboard holds no image (or on platforms without clipboard image support).
  /// </summary>
  public static async Task<List<ClipboardImage>> GetImagesAsync()
  {
    var images = new List<ClipboardImage>();
#if WINDOWS
    Windows.ApplicationModel.DataTransfer.DataPackageView content;
    try
    {
      content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
    }
    catch
    {
      return images;
    }

    if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
    {
      // Clipboard bitmaps come in an unspecified format — re-encode to PNG so the
      // stored file has a real image extension (MediaStorage keys the type off it).
      var streamRef = await content.GetBitmapAsync();
      using var source = await streamRef.OpenReadAsync();
      var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(source);
      using var bitmap = await decoder.GetSoftwareBitmapAsync(
          Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
          Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

      using var encoded = new Windows.Storage.Streams.InMemoryRandomAccessStream();
      var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
          Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, encoded);
      encoder.SetSoftwareBitmap(bitmap);
      await encoder.FlushAsync();

      var ms = new MemoryStream();
      encoded.Seek(0);
      await encoded.AsStreamForRead().CopyToAsync(ms);
      ms.Position = 0;
      images.Add(new ClipboardImage(ms, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
      return images;
    }

    if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
    {
      var items = await content.GetStorageItemsAsync();
      foreach (var file in items.OfType<Windows.Storage.StorageFile>())
      {
        if (file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
          continue;
        using var source = (await file.OpenReadAsync()).AsStreamForRead();
        var ms = new MemoryStream();
        await source.CopyToAsync(ms);
        ms.Position = 0;
        images.Add(new ClipboardImage(ms, file.Name));
      }
    }
#elif ANDROID
    await Task.Run(() =>
    {
      var context = Android.App.Application.Context;
      var clipboard = context.GetSystemService(Android.Content.Context.ClipboardService)
          as Android.Content.ClipboardManager;
      var clip = clipboard?.PrimaryClip;
      if (clip == null) return;

      for (int i = 0; i < clip.ItemCount; i++)
      {
        try
        {
          var uri = clip.GetItemAt(i)?.Uri;
          if (uri == null) continue;

          var resolver = context.ContentResolver!;
          var mime = resolver.GetType(uri);
          if (mime == null || !mime.StartsWith("image/")) continue;

          using var source = resolver.OpenInputStream(uri);
          if (source == null) continue;

          var ms = new MemoryStream();
          source.CopyTo(ms);
          ms.Position = 0;

          var ext = Android.Webkit.MimeTypeMap.Singleton?.GetExtensionFromMimeType(mime) ?? "png";
          images.Add(new ClipboardImage(ms, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}_{i}.{ext}"));
        }
        catch
        {
          // No read permission for this URI or the source app revoked it — skip the item.
        }
      }
    });
#else
    await Task.CompletedTask;
#endif
    return images;
  }
}
