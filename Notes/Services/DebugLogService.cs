using System.Collections.Concurrent;
using System.Text;

namespace Notes.Services;

public class DebugLogService
{
  // Accessible statically so non-injected classes (SyncApiClient) can log without DI wiring.
  internal static DebugLogService? Current { get; private set; }

  private const int MaxEntries = 5000;
  private readonly ConcurrentQueue<string> _entries = new();
  private readonly object _fileLock = new();

  // Only used in Windows debug builds.
  private StreamWriter? _fileWriter;

  public DebugLogService() => Current = this;

  public void Log(string message)
  {
    var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
    _entries.Enqueue(entry);
    while (_entries.Count > MaxEntries)
      _entries.TryDequeue(out _);
#if DEBUG && WINDOWS
    lock (_fileLock)
      try { _fileWriter?.WriteLine(entry); _fileWriter?.Flush(); } catch { }
#endif
  }

  // Windows debug builds: write every log entry to a file in real time.
  public void StartFileLogging(string filePath)
  {
#if DEBUG && WINDOWS
    lock (_fileLock)
    {
      try
      {
        _fileWriter?.Dispose();
        _fileWriter = new StreamWriter(filePath, append: false, Encoding.UTF8);
        Log("=== session start ===");
      }
      catch { }
    }
#endif
  }

  // Export the in-memory ring buffer. Works in both debug and release builds.
  public string GetLogsText() => string.Join('\n', _entries);
}
