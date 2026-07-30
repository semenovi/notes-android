namespace Notes.Services;

public class ProgressNotificationService
{
    private readonly List<ProgressSession> _active = new();
    public ProgressSession? Current { get; private set; }

    public event Action<ProgressSession>? ShowRequested;
    public event Action<ProgressSession>? UpdateRequested;
    public event Action? HideRequested;

    public ProgressSession Begin(string title, int delayMs = 2000, int priority = 0)
        => new(title, delayMs, priority, this);

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
    private System.Threading.Timer? _timer;
    private bool _shown, _done;

    public string Title { get; }
    public int Priority { get; }
    public double Progress { get; private set; } = double.NaN;
    public string? Subtitle { get; private set; }

    internal ProgressSession(string title, int delayMs, int priority, ProgressNotificationService svc)
    {
        Title = title;
        Priority = priority;
        _svc = svc;
        if (delayMs <= 0)
        {
            _shown = true;
            svc.OnShow(this);
        }
        else
        {
            _timer = new System.Threading.Timer(_ => TryShow(), null, delayMs, Timeout.Infinite);
        }
    }

    private void TryShow()
    {
        _timer?.Dispose(); _timer = null;
        if (_done) return;
        _shown = true;
        _svc.OnShow(this);
    }

    public void Report(double progress, string? subtitle = null)
    {
        Progress = progress;
        Subtitle = subtitle;
        if (_shown) _svc.OnUpdate(this);
    }

    public void Dispose()
    {
        if (_done) return;
        _done = true;
        _timer?.Dispose(); _timer = null;
        bool wasShown = _shown;
        _shown = false;
        if (wasShown) _svc.OnHide(this);
    }
}
