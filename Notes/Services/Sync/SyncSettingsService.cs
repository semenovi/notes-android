using System.Text.Json;
using Notes.Models;

namespace Notes.Services.Sync;

public class SyncSettingsService
{
  private readonly string _settingsPath;
  private SyncSettings? _cached;

  public SyncSettingsService()
  {
    string dir = Path.Combine(FileSystem.AppDataDirectory, "Notes");
    Directory.CreateDirectory(dir);
    _settingsPath = Path.Combine(dir, "sync_settings.json");
  }

  public async Task<SyncSettings> LoadAsync()
  {
    if (_cached != null) return _cached;
    try
    {
      if (File.Exists(_settingsPath))
      {
        string json = await File.ReadAllTextAsync(_settingsPath);
        _cached = JsonSerializer.Deserialize<SyncSettings>(json) ?? new SyncSettings();
        return _cached;
      }
    }
    catch { }
    _cached = new SyncSettings();
    return _cached;
  }

  public async Task SaveAsync(SyncSettings settings)
  {
    _cached = settings;
    await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(settings));
  }

  public void InvalidateCache() => _cached = null;
}
