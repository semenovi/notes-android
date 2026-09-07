namespace Notes.Helpers;

public static class MediaFilePickerTypes
{
  public static readonly FilePickerFileType ImagesAndPdf = new(new Dictionary<DevicePlatform, IEnumerable<string>>
  {
    { DevicePlatform.Android, new[] { "image/*", "application/pdf" } },
    { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".pdf" } },
  });
}
