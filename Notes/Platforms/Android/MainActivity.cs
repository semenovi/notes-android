using Android.App;
using Android.Content.PM;
using Android.Views;

namespace Notes;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private float _downX, _downY;
    private bool _tracking;
    private bool _confirmed;

    private const float ThresholdPx = 16f;

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e == null || Notes.Platforms.Android.SwipeBackGesture.OnProgress == null)
            return base.DispatchTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = e.GetX();
                _downY = e.GetY();
                _tracking = true;
                _confirmed = false;
                break;

            case MotionEventActions.Move:
                if (!_tracking) break;

                float dx = e.GetX() - _downX;
                float dy = e.GetY() - _downY;

                if (!_confirmed)
                {
                    if (Math.Abs(dx) < ThresholdPx && Math.Abs(dy) < ThresholdPx)
                        break;

                    if (dx > 0 && Math.Abs(dx) > Math.Abs(dy))
                    {
                        _confirmed = true;
                        using var cancel = MotionEvent.Obtain(e);
                        cancel!.Action = MotionEventActions.Cancel;
                        base.DispatchTouchEvent(cancel);
                    }
                    else
                    {
                        _tracking = false;
                        break;
                    }
                }

                if (_confirmed)
                {
                    float density = Resources?.DisplayMetrics?.Density ?? 1f;
                    Notes.Platforms.Android.SwipeBackGesture.OnProgress?.Invoke(dx / density);
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
                break;

            case MotionEventActions.Cancel:
                if (_confirmed)
                    Notes.Platforms.Android.SwipeBackGesture.OnCancel?.Invoke();
                _confirmed = false;
                _tracking = false;
                break;
        }

        return base.DispatchTouchEvent(e);
    }
}
