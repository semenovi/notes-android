namespace Notes.Helpers;

// Android 12+ renders the system-wide stretch overscroll for NestedScrollView / WebView
// automatically, but only when the native overScrollMode allows it. MAUI leaves the mode
// at the platform default (ifContentScrolls), so short content never stretches. Forcing
// Always makes the edge bounce back on every boundary drag, matching the rest of the OS.
public static class OverscrollStretch
{
  public static void Enable(VisualElement element)
  {
#if ANDROID
    if (element == null)
      return;

    if (element.Handler?.PlatformView is Android.Views.View native)
    {
      native.OverScrollMode = Android.Views.OverScrollMode.Always;
      return;
    }

    element.HandlerChanged -= OnHandlerChanged;
    element.HandlerChanged += OnHandlerChanged;
#endif
  }

#if ANDROID
  private static void OnHandlerChanged(object sender, EventArgs e)
  {
    if (sender is VisualElement element && element.Handler?.PlatformView is Android.Views.View native)
    {
      native.OverScrollMode = Android.Views.OverScrollMode.Always;
      element.HandlerChanged -= OnHandlerChanged;
    }
  }
#endif
}
