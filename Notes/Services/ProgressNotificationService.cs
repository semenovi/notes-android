using System.Linq;

namespace Notes.Services;

public enum ProgressScope
{
    App,
    Page,
}

public class ProgressNotificationService
{
    // Backstop lifetime for a session whose owner never disposes it (hung await,
    // page torn down mid-load, network stall). The underlying operation keeps running;
    // only the overlay is dismissed.
    public const int DefaultMaxDurationMs = 120_000;

    private readonly List<ProgressSession> _active = new();
    public ProgressSession? Current { get; private set; }

    public event Action<ProgressSession>? ShowRequested;
    public event Action<ProgressSession>? UpdateRequested;
    public event Action? HideRequested;

    public ProgressSession Begin(string title, int delayMs = 2000, int priority = 0,
        int maxDurationMs = DefaultMaxDurationMs, int idleTimeoutMs = 0,
        ProgressScope scope = ProgressScope.App)
        => new(title, delayMs, priority, maxDurationMs, idleTimeoutMs, scope, this);

    // Dismisses every visible session. Does NOT cancel the work behind them — used on
    // app background, where an in-app overlay has no viewer and would only reappear stale.
    public void CancelAll()
    {
        ProgressSession[] snapshot;
        lock (_active) snapshot = _active.ToArray();
        foreach (var s in snapshot) s.Dispose();
    }

    public void CancelScope(ProgressScope scope)
    {
        ProgressSession[] snapshot;
        lock (_active) snapshot = _active.Where(s => s.Scope == scope).ToArray();
        foreach (var s in snapshot) s.Dispose();
    }

    internal void OnShow(ProgressSession s)
    {
        lock (_active)
        {
            if (!_active.Contains(s)) _active.Add(s);
            Refresh();
        }
    }

    internal void OnUpdate(ProgressSession s)
    {
        if (Current != s) return;
        MainThread.BeginInvokeOnMainThread(() => UpdateRequested?.Invoke(s));
    }

    internal void OnHide(ProgressSession s)
    {
        lock (_active)
        {
            _active.Remove(s);
            Refresh();
        }
    }

    // Called under lock. The highest-priority active session is displayed;
    // a lower-priority one that finishes while hidden never touches the UI,
    // and the previous session is restored when the top one ends. On a priority tie,
    // the session already showing keeps winning (strict '>', not '>=') — otherwise a
    // second session for the same operation (e.g. two overlapping sync triggers) that
    // never gets to report real progress would keep bumping the one that's actually
    // reporting, permanently freezing the UI on an indeterminate spinner.
    private void Refresh()
    {
        ProgressSession? top = null;
        foreach (var s in _active)
            if (top == null || s.Priority > top.Priority) top = s;
        if (Current != null && _active.Contains(Current) &&
            (top == null || Current.Priority >= top.Priority))
            top = Current;

        if (top == Current) return;
        Current = top;
        if (top == null)
            MainThread.BeginInvokeOnMainThread(() => HideRequested?.Invoke());
        else
            MainThread.BeginInvokeOnMainThread(() => ShowRequested?.Invoke(top));
    }
}

public sealed class ProgressSession : IDisposable
{
    private readonly ProgressNotificationService _svc;
    private readonly object _gate = new();
    private readonly int _idleTimeoutMs;
    private System.Threading.Timer? _delayTimer;
    private System.Threading.Timer? _maxTimer;
    private System.Threading.Timer? _idleTimer;
    private bool _shown, _done;

    public string Title { get; }
    public int Priority { get; }
    public ProgressScope Scope { get; }
    public double Progress { get; private set; } = double.NaN;
    public string? Subtitle { get; private set; }
    public bool IsActive => !_done;

    internal ProgressSession(string title, int delayMs, int priority, int maxDurationMs,
        int idleTimeoutMs, ProgressScope scope, ProgressNotificationService svc)
    {
        Title = title;
        Priority = priority;
        Scope = scope;
        _idleTimeoutMs = idleTimeoutMs;
        _svc = svc;

        if (delayMs <= 0)
        {
            _shown = true;
            svc.OnShow(this);
        }
        else
        {
            _delayTimer = new System.Threading.Timer(_ => TryShow(), null, delayMs, Timeout.Infinite);
        }

        if (maxDurationMs > 0)
            _maxTimer = new System.Threading.Timer(_ => Finish(timedOut: true, reason: "max-duration"),
                null, maxDurationMs, Timeout.Infinite);
        if (idleTimeoutMs > 0)
            _idleTimer = new System.Threading.Timer(_ => Finish(timedOut: true, reason: "idle"),
                null, idleTimeoutMs, Timeout.Infinite);
    }

    private void TryShow()
    {
        lock (_gate)
        {
            if (_done) return;
            _delayTimer?.Dispose(); _delayTimer = null;
            _shown = true;
        }
        _svc.OnShow(this);
    }

    public void Report(double progress, string? subtitle = null)
    {
        Progress = progress;
        Subtitle = subtitle;
        if (_idleTimeoutMs > 0)
        {
            lock (_gate)
            {
                if (!_done)
                    _idleTimer?.Change(_idleTimeoutMs, Timeout.Infinite);
            }
        }
        if (_shown) _svc.OnUpdate(this);
    }

    public void Dispose() => Finish(timedOut: false, reason: null);

    private void Finish(bool timedOut, string? reason)
    {
        bool wasShown;
        lock (_gate)
        {
            if (_done) return;
            _done = true;
            _delayTimer?.Dispose(); _delayTimer = null;
            _maxTimer?.Dispose(); _maxTimer = null;
            _idleTimer?.Dispose(); _idleTimer = null;
            wasShown = _shown;
            _shown = false;
        }
        if (timedOut)
            DebugLogService.Current?.Log($"progress-session-timeout: '{Title}' ({reason})");
        if (wasShown) _svc.OnHide(this);
    }
}
