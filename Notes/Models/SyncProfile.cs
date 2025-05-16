namespace Notes.Models;

public class SyncProfile
{
  public string Id { get; set; } = Guid.NewGuid().ToString();
  public string Name { get; set; } = string.Empty;
  public SyncProtocolType Protocol { get; set; }
  public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
}