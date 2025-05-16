namespace Notes.Models;

public class Note
{
  public string Id { get; set; } = Guid.NewGuid().ToString();
  public string Title { get; set; } = string.Empty;
  public string Content { get; set; } = string.Empty;
  public DateTime Created { get; set; } = DateTime.Now;
  public DateTime Modified { get; set; } = DateTime.Now;
  public string FolderId { get; set; } = string.Empty;
  public List<string> Tags { get; set; } = new List<string>();
}