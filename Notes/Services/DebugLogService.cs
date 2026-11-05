using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Notes.Services;

public class DebugLogService
{

  internal static DebugLogService? Current { get; private set; }

  private const int MaxEntries = 5000;
  private readonly ConcurrentQueue<string> _entries = new();
  private readonly object _fileLock = new();

  private StreamWriter? _fileWriter;

  public DebugLogService() => Current = this;

  [Conditional("DEBUG")]
  public void Log(string message)
  {
    var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
    _entries.Enqueue(entry);
    while (_entries.Count > MaxEntries)
      _entries.TryDequeue(out _);
#if WINDOWS
    lock (_fileLock)
      try { _fileWriter?.WriteLine(entry); _fileWriter?.Flush(); } catch { }
#endif

    Debug.WriteLine("[notes-sync] " + entry);
  }

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

  public string GetLogsText() => string.Join('\n', _entries);
}
