namespace Notes.Models;

public class MediaItem
{
  public string Id { get; set; } = Guid.NewGuid().ToString();
  public string FileName { get; set; } = string.Empty;
  public string FileType { get; set; } = string.Empty;
  public string StoragePath { get; set; } = string.Empty;
  public long Size { get; set; }
  public DateTime Created { get; set; } = DateTime.Now;
}