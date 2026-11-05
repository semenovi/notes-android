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

    private bool _dragArmed;
    private bool _dragConfirmed;
    private long _downTimeMs;
    private Handler? _longPressHandler;
    private Java.Lang.Runnable? _longPressRunnable;

    private const long LongPressMs = 450;
    private const float DragMoveThresholdPx = 24f;

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

                if (_tracking && !_confirmed && !_dragConfirmed)
                {
                    if (Math.Abs(dx) < ThresholdPx && Math.Abs(dy) < ThresholdPx)
                    {

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
                        CancelDragArm(); 
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

    private void OnLongPressTimerFired()
    {
        if (!_dragArmed || _dragConfirmed) return;
        _dragConfirmed = true;
        _dragArmed = false;
        _tracking = false; 

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
