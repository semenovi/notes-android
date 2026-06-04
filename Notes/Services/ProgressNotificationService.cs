namespace Notes.Services;

public class ProgressNotificationService
{
    public ProgressSession? Current { get; private set; }

    public event Action<ProgressSession>? ShowRequested;
    public event Action<ProgressSession>? UpdateRequested;
    public event Action? HideRequested;

    public ProgressSession Begin(string title, int delayMs = 2000)
        => new(title, delayMs, this);

    internal void OnShow(ProgressSession s)
    {
        Current = s;
        MainThread.BeginInvokeOnMainThread(() => ShowRequested?.Invoke(s));
    }

    internal void OnUpdate(ProgressSession s)
        => MainThread.BeginInvokeOnMainThread(() => UpdateRequested?.Invoke(s));

    internal void OnHide(ProgressSession s)
    {
        if (Current == s) Current = null;
        MainThread.BeginInvokeOnMainThread(() => HideRequested?.Invoke());
    }
}

public sealed class ProgressSession : IDisposable
{
    private readonly ProgressNotificationService _svc;
    private System.Threading.Timer? _timer;
    private bool _shown, _done;

    public string Title { get; }
    public double Progress { get; private set; } = double.NaN;
    public string? Subtitle { get; private set; }

    internal ProgressSession(string title, int delayMs, ProgressNotificationService svc)
    {
        Title = title;
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
