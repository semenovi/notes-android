namespace Notes.Helpers;

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
