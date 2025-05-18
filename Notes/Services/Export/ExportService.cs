using System.IO.Compression;
using Notes.Data.Storage;
using CommunityToolkit.Maui.Storage;

namespace Notes.Services.Export;

public class ExportService
{
  private readonly FileSystemStorage _storage;
  private readonly IFileSaver _fileSaver;

  public ExportService(FileSystemStorage storage, IFileSaver fileSaver)
  {
    _storage = storage;
    _fileSaver = fileSaver;
  }

  public async Task<string> CreateBackupAsync()
  {
    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
    string tempFileName = $"{timestamp}.zip";
    string tempPath = Path.Combine(FileSystem.CacheDirectory, tempFileName);

    try
    {
      using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
      {
        await AddFolderToArchiveAsync(archive, _storage.RootPath, "");
      }

      return tempPath;
    }
    catch (Exception ex)
    {
      if (File.Exists(tempPath))
        File.Delete(tempPath);

      throw new Exception($"Failed to create backup: {ex.Message}", ex);
    }
  }

  private async Task AddFolderToArchiveAsync(ZipArchive archive, string folderPath, string entryPath)
  {
    foreach (var file in Directory.GetFiles(folderPath))
    {
      string fileName = Path.GetFileName(file);
      string entryName = string.IsNullOrEmpty(entryPath) ? fileName : Path.Combine(entryPath, fileName);

      archive.CreateEntryFromFile(file, entryName);
    }

    foreach (var dir in Directory.GetDirectories(folderPath))
    {
      string dirName = new DirectoryInfo(dir).Name;
      string newEntryPath = string.IsNullOrEmpty(entryPath) ? dirName : Path.Combine(entryPath, dirName);

      await AddFolderToArchiveAsync(archive, dir, newEntryPath);
    }
  }

  public async Task<string> ExportBackupAsync()
  {
    string backupPath = null;

    try
    {
      backupPath = await CreateBackupAsync();
      string fileName = Path.GetFileName(backupPath);

      using var stream = new MemoryStream(await File.ReadAllBytesAsync(backupPath));
      var result = await _fileSaver.SaveAsync(fileName, stream, CancellationToken.None);

      if (result.IsSuccessful)
      {
        return result.FilePath;
      }

      throw new Exception("Backup not saved. Operation was cancelled.");
    }
    catch (Exception ex)
    {
      throw new Exception($"Export failed: {ex.Message}", ex);
    }
    finally
    {
      if (backupPath != null && File.Exists(backupPath))
        File.Delete(backupPath);
    }
  }

  public async Task<bool> ImportBackupAsync(string backupPath)
  {
    if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
      throw new FileNotFoundException("Backup file not found", backupPath);

    string tempDir = Path.Combine(FileSystem.CacheDirectory, "import_temp");

    try
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);

      Directory.CreateDirectory(tempDir);

      ZipFile.ExtractToDirectory(backupPath, tempDir);

      await ClearExistingDataAsync();

      await CopyImportedFilesToStorageAsync(tempDir, _storage.RootPath);

      return true;
    }
    catch (Exception ex)
    {
      throw new Exception($"Failed to import backup: {ex.Message}", ex);
    }
    finally
    {
      if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
    }
  }

  private async Task ClearExistingDataAsync()
  {
    string[] foldersToDelete = { "Notes", "Folders", "Media", "Media/Metadata" };

    foreach (var folder in foldersToDelete)
    {
      string path = Path.Combine(_storage.RootPath, folder);

      if (Directory.Exists(path))
      {
        foreach (var file in Directory.GetFiles(path))
        {
          File.Delete(file);
        }
      }
    }

    await Task.CompletedTask;
  }

  private async Task CopyImportedFilesToStorageAsync(string sourcePath, string targetPath)
  {
    if (!Directory.Exists(targetPath))
      Directory.CreateDirectory(targetPath);

    foreach (var file in Directory.GetFiles(sourcePath))
    {
      string fileName = Path.GetFileName(file);
      string destFile = Path.Combine(targetPath, fileName);

      File.Copy(file, destFile, true);
    }

    foreach (var dir in Directory.GetDirectories(sourcePath))
    {
      string dirName = new DirectoryInfo(dir).Name;
      string destDir = Path.Combine(targetPath, dirName);

      if (!Directory.Exists(destDir))
        Directory.CreateDirectory(destDir);

      await CopyImportedFilesToStorageAsync(dir, destDir);
    }
  }
}