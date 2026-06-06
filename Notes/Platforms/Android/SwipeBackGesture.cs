namespace Notes.Platforms.Android;

internal static class SwipeBackGesture
{
    internal static Action<float>? OnProgress;
    internal static Action<float>? OnEnd;
    internal static Action? OnCancel;
}
