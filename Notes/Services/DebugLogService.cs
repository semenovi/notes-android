using System.Collections.Concurrent;
using System.Diagnostics;
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

  // [Conditional("DEBUG")] strips every call site (including argument evaluation — the
  // interpolated strings callers pass in never get built) when the calling code isn't
  // compiled with the DEBUG symbol, i.e. in Release builds this whole call disappears
  // for free. Sync now logs a lot (per-item, sometimes per-chunk); that volume is only
  // ever acceptable while actively debugging, never in a build a user runs day to day.
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
    // Mirrors every entry to the IDE's Debug/Output window, for reading live while
    // debugging instead of only after the fact via GetLogsText().
    Debug.WriteLine("[notes-sync] " + entry);
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

  // Debug builds only — Log() is compiled out entirely in Release, so the ring buffer
  // stays empty there and this returns "".
  public string GetLogsText() => string.Join('\n', _entries);
}
