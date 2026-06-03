namespace Notes.Models;

public class Folder
{
  public string Id { get; set; } = Guid.NewGuid().ToString();
  public string Name { get; set; } = string.Empty;
  public string? ParentId { get; set; } = null;
  public DateTime Modified { get; set; } = DateTime.Now;
  public string Icon { get; set; } = "";
}
