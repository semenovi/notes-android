namespace Notes.Models;

public class SyncSettings
{
  public bool Enabled { get; set; } = false;
  public string ServerUrl { get; set; } = string.Empty;
  public string ApiToken { get; set; } = string.Empty;
  public string DeviceId { get; set; } = Guid.NewGuid().ToString();
}
