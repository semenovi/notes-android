namespace Notes.Helpers;

public static class MediaFilePickerTypes
{

  public static readonly FilePickerFileType ImagesAndPdf = new(new Dictionary<DevicePlatform, IEnumerable<string>>
  {
    { DevicePlatform.Android, new[] { "image/*", "application/pdf" } },
    { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".pdf" } },
  });

  public static readonly FilePickerFileType ImagesOnly = new(new Dictionary<DevicePlatform, IEnumerable<string>>
  {
    { DevicePlatform.Android, new[] { "image/*" } },
    { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" } },
  });

  public static readonly FilePickerFileType PdfOnly = new(new Dictionary<DevicePlatform, IEnumerable<string>>
  {
    { DevicePlatform.Android, new[] { "application/pdf" } },
    { DevicePlatform.WinUI, new[] { ".pdf" } },
  });
}
