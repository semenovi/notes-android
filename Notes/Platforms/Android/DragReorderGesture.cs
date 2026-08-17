namespace Notes.Platforms.Android;

// Coordinates are raw screen pixels (not DP), matching what MainActivity reads
// off the native MotionEvent — callers divide by density themselves.
internal static class DragReorderGesture
{
    internal static Action<float, float>? OnLongPress;
    internal static Action<float, float>? OnMove;
    internal static Action<float, float>? OnEnd;
    internal static Action? OnCancel;
}
