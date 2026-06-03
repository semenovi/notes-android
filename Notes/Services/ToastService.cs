namespace Notes.Services;

public class ToastService
{
    public event Action<string>? ToastRequested;

    public void Show(string message)
        => MainThread.BeginInvokeOnMainThread(() => ToastRequested?.Invoke(message));
}
