using System.Text.Json;

namespace Notes.Data.Storage;

public class FileSystemStorage
{
  private readonly string _rootPath;

  public FileSystemStorage(string rootPath)
  {
    _rootPath = rootPath;
    EnsureDirectoryExists(_rootPath);
  }

  public string RootPath => _rootPath;

  public async Task<byte[]> ReadFileAsync(string path)
  {
    string fullPath = Path.Combine(_rootPath, path);
    return await File.ReadAllBytesAsync(fullPath);
  }

  public async Task WriteFileAsync(string path, byte[] data)
  {
    string fullPath = Path.Combine(_rootPath, path);
    string directory = Path.GetDirectoryName(fullPath);

    if (!string.IsNullOrEmpty(directory))
      EnsureDirectoryExists(directory);

    await File.WriteAllBytesAsync(fullPath, data);
  }

  public async Task<bool> DeleteFileAsync(string path)
  {
    string fullPath = Path.Combine(_rootPath, path);

    if (!File.Exists(fullPath))
      return false;

    try
    {
      File.Delete(fullPath);
      return true;
    }
    catch
    {
      return false;
    }
  }

  public List<string> GetFiles(string directory)
  {
    string fullPath = Path.Combine(_rootPath, directory);

    if (!Directory.Exists(fullPath))
      return new List<string>();

    return Directory.GetFiles(fullPath)
        .Select(f => Path.GetRelativePath(_rootPath, f))
        .ToList();
  }

  public async Task<T?> ReadJsonAsync<T>(string path)
  {
    try
    {
      byte[] data = await ReadFileAsync(path);
      string json = System.Text.Encoding.UTF8.GetString(data);
      return JsonSerializer.Deserialize<T>(json);
    }
    catch
    {
      return default;
    }
  }

  public async Task WriteJsonAsync<T>(string path, T data)
  {
    string json = JsonSerializer.Serialize(data);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
    await WriteFileAsync(path, bytes);
  }

  private void EnsureDirectoryExists(string directory)
  {
    if (!Directory.Exists(directory))
      Directory.CreateDirectory(directory);
  }
}