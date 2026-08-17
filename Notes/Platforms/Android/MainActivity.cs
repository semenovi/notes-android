using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Notes;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    private float _downX, _downY;
    private bool _tracking;
    private bool _confirmed;

    private const float ThresholdPx = 16f;

    // Long-press-then-drag (used for reordering notes/folders between folders).
    // Deliberately bypasses Android's native View.startDragAndDrop/OnDragListener —
    // that API has a long-standing platform bug (ConcurrentModificationException in
    // ViewGroup.dispatchDragEvent) that fires whenever the view hierarchy changes
    // anywhere nearby a live native drag session, which a reactive MAUI UI does
    // constantly. Tracking raw touch here, like the swipe-back gesture below, never
    // touches that code path at all.
    private bool _dragArmed;
    private bool _dragConfirmed;
    private long _downTimeMs;
    private Handler? _longPressHandler;
    private Java.Lang.Runnable? _longPressRunnable;

    private const long LongPressMs = 450;
    private const float DragMoveThresholdPx = 24f;

    /// <summary>
    /// the manifest value is overridden at runtime (the window ends up in pan mode, which
    /// scrolls the whole window to reveal the caret and drags the shell bar and the editor
    /// toolbar off screen), so re-assert resize once maui is up. resizing keeps the content
    /// above the keyboard, which lets the editor reveal the caret on its own.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        bool hasSwipeHandlers = Notes.Platforms.Android.SwipeBackGesture.OnProgress != null;
        bool hasDragHandlers = Notes.Platforms.Android.DragReorderGesture.OnLongPress != null;
        if (e == null || (!hasSwipeHandlers && !hasDragHandlers))
            return base.DispatchTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = e.GetX();
                _downY = e.GetY();
                _tracking = hasSwipeHandlers;
                _confirmed = false;

                _dragArmed = hasDragHandlers;
                _dragConfirmed = false;
                if (_dragArmed)
                {
                    _downTimeMs = SystemClock.UptimeMillis();
                    _longPressHandler ??= new Handler(Looper.MainLooper!);
                    _longPressRunnable = new Java.Lang.Runnable(OnLongPressTimerFired);
                    _longPressHandler.PostDelayed(_longPressRunnable, LongPressMs);
                }
                break;

            case MotionEventActions.Move:
                float dx = e.GetX() - _downX;
                float dy = e.GetY() - _downY;

                // Once a drag is confirmed it owns the gesture for good — without this
                // guard, a later rightward move (very plausible mid-drag: the note is
                // being carried across the screen) could still flip _confirmed here and
                // silently steal the touch stream, leaving the drag confirmed on the
                // MAUI side forever with no matching End/Cancel ever delivered (ghost
                // stuck, IsDragActive stuck true, every animation gated on it silently
                // skipped from then on).
                if (_tracking && !_confirmed && !_dragConfirmed)
                {
                    if (Math.Abs(dx) < ThresholdPx && Math.Abs(dy) < ThresholdPx)
                    {
                        // below the swipe-confirm threshold — fall through to the
                        // drag-arm check below, don't break early like before.
                    }
                    else if (dx > 0 && Math.Abs(dx) > Math.Abs(dy))
                    {
                        _confirmed = true;
                        CancelDragArm();
                        using var cancel = MotionEvent.Obtain(e);
                        cancel!.Action = MotionEventActions.Cancel;
                        base.DispatchTouchEvent(cancel);
                    }
                    else
                    {
                        _tracking = false;
                    }
                }

                if (_confirmed)
                {
                    float density = Resources?.DisplayMetrics?.Density ?? 1f;
                    Notes.Platforms.Android.SwipeBackGesture.OnProgress?.Invoke(dx / density);
                    return true;
                }

                if (_dragArmed && !_dragConfirmed)
                {
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist >= DragMoveThresholdPx)
                        CancelDragArm(); // moved too far before the hold completed — a scroll/fling, not a pickup
                }

                if (_dragConfirmed)
                {
                    Notes.Platforms.Android.DragReorderGesture.OnMove?.Invoke(e.GetX(), e.GetY());
                    return true;
                }
                break;

            case MotionEventActions.Up:
                if (_confirmed)
                {
                    _confirmed = false;
                    _tracking = false;
                    float density = Resources?.DisplayMetrics?.Density ?? 1f;
                    Notes.Platforms.Android.SwipeBackGesture.OnEnd?.Invoke((e.GetX() - _downX) / density);
                    return true;
                }
                _tracking = false;

                if (_dragConfirmed)
                {
                    _dragConfirmed = false;
                    Notes.Platforms.Android.DragReorderGesture.OnEnd?.Invoke(e.GetX(), e.GetY());
                    return true;
                }
                CancelDragArm();
                break;

            case MotionEventActions.Cancel:
                if (_confirmed)
                    Notes.Platforms.Android.SwipeBackGesture.OnCancel?.Invoke();
                _confirmed = false;
                _tracking = false;

                if (_dragConfirmed)
                    Notes.Platforms.Android.DragReorderGesture.OnCancel?.Invoke();
                _dragConfirmed = false;
                CancelDragArm();
                break;
        }

        return base.DispatchTouchEvent(e);
    }

    // Fires ~450ms after Down if the finger is still down and hasn't moved past the
    // threshold — timer-driven rather than derived from Move events because a
    // genuinely still hold produces no Move events at all to check elapsed time from.
    private void OnLongPressTimerFired()
    {
        if (!_dragArmed || _dragConfirmed) return;
        _dragConfirmed = true;
        _dragArmed = false;
        _tracking = false; // a drag owns the gesture now — swipe-back must not engage anymore

        long now = SystemClock.UptimeMillis();
        using var cancel = MotionEvent.Obtain(_downTimeMs, now, MotionEventActions.Cancel, _downX, _downY, 0);
        base.DispatchTouchEvent(cancel);

        Notes.Platforms.Android.DragReorderGesture.OnLongPress?.Invoke(_downX, _downY);
    }

    private void CancelDragArm()
    {
        _dragArmed = false;
        if (_longPressRunnable != null)
        {
            _longPressHandler?.RemoveCallbacks(_longPressRunnable);
            _longPressRunnable = null;
        }
    }
}
