namespace Notes.Platforms.Android;

internal static class DragReorderGesture
{
    internal static Action<float, float>? OnLongPress;
    internal static Action<float, float>? OnMove;
    internal static Action<float, float>? OnEnd;
    internal static Action? OnCancel;
}
