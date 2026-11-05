namespace Notes.Helpers;

public static class AppPaths
{
  private const string DataOverrideVariable = "NOTES_DATA_DIR";
  private static readonly bool _hasOverride =
      !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataOverrideVariable));
  private static string? _dataRoot;

  public static bool HasOverride => _hasOverride;

  public static string DataRoot => _dataRoot ??= ResolveDataRoot();

  public static string CacheRoot
  {
    get
    {
      string path = _hasOverride
          ? Path.Combine(DataRoot, "Cache")
          : FileSystem.CacheDirectory;
      Directory.CreateDirectory(path);
      return path;
    }
  }

  private static string ResolveDataRoot()
  {
    string? overridePath = Environment.GetEnvironmentVariable(DataOverrideVariable);
    string path = string.IsNullOrWhiteSpace(overridePath)
        ? Path.Combine(FileSystem.AppDataDirectory, "Notes")
        : Path.GetFullPath(overridePath);
    Directory.CreateDirectory(path);
    return path;
  }
}
