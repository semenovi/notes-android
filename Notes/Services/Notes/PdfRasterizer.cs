namespace Notes.Services.Notes;

// Renders PDF pages to PNG bytes via PDFium on both platforms: Android's built-in
// android.graphics.pdf.PdfRenderer, and the PDFtoImage package on Windows. Windows.Data.Pdf
// was tried first and dropped — it renders some pages as a blank white bitmap with no error.
public static class PdfRasterizer
{
#if ANDROID
  public static Task<int> GetPageCountAsync(byte[] pdfBytes) => Task.Run(() =>
  {
    using var renderer = OpenRenderer(pdfBytes, out var pfd, out var tempFile);
    try { return renderer.PageCount; }
    finally { pfd.Close(); DeleteQuiet(tempFile); }
  });

  public static Task<byte[]> RenderPageAsync(byte[] pdfBytes, int pageIndex, int maxDimensionPx) => Task.Run(() =>
  {
    using var renderer = OpenRenderer(pdfBytes, out var pfd, out var tempFile);
    try
    {
      using var page = renderer.OpenPage(pageIndex);
      float scale = (float)maxDimensionPx / Math.Max(page.Width, page.Height);
      int w = Math.Max(1, (int)(page.Width * scale));
      int h = Math.Max(1, (int)(page.Height * scale));
      using var bitmap = Android.Graphics.Bitmap.CreateBitmap(w, h, Android.Graphics.Bitmap.Config.Argb8888!);
      bitmap.EraseColor(Android.Graphics.Color.White);
      page.Render(bitmap, null, null, Android.Graphics.Pdf.PdfRenderMode.ForDisplay);
      using var ms = new MemoryStream();
      bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 100, ms);
      return ms.ToArray();
    }
    finally { pfd.Close(); DeleteQuiet(tempFile); }
  });

  private static Android.Graphics.Pdf.PdfRenderer OpenRenderer(byte[] pdfBytes,
      out Android.OS.ParcelFileDescriptor pfd, out string tempFile)
  {
    tempFile = Path.Combine(FileSystem.CacheDirectory, $"pdf-render-{Guid.NewGuid():N}.pdf");
    File.WriteAllBytes(tempFile, pdfBytes);
    pfd = Android.OS.ParcelFileDescriptor.Open(new Java.IO.File(tempFile), Android.OS.ParcelFileMode.ReadOnly)!;
    return new Android.Graphics.Pdf.PdfRenderer(pfd);
  }

  private static void DeleteQuiet(string path)
  {
    try { File.Delete(path); } catch { }
  }
#elif WINDOWS
  public static Task<int> GetPageCountAsync(byte[] pdfBytes) =>
      Task.Run(() => PDFtoImage.Conversion.GetPageCount(pdfBytes));

  public static Task<byte[]> RenderPageAsync(byte[] pdfBytes, int pageIndex, int maxDimensionPx) => Task.Run(() =>
  {
    var size = PDFtoImage.Conversion.GetPageSize(pdfBytes, pageIndex);
    // constrain the longer side, letting PDFium derive the other from the aspect ratio
    var options = size.Width >= size.Height
        ? new PDFtoImage.RenderOptions(Width: maxDimensionPx, WithAspectRatio: true)
        : new PDFtoImage.RenderOptions(Height: maxDimensionPx, WithAspectRatio: true);

    using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: pageIndex, options: options);
    using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    return data.ToArray();
  });
#else
  public static Task<int> GetPageCountAsync(byte[] pdfBytes) => Task.FromResult(0);
  public static Task<byte[]> RenderPageAsync(byte[] pdfBytes, int pageIndex, int maxDimensionPx) =>
      Task.FromResult(Array.Empty<byte>());
#endif
}
