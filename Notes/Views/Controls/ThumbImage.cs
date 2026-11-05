namespace Notes.Views.Controls;

public sealed class ThumbImage : Image
{
  protected override void OnHandlerChanged()
  {
    base.OnHandlerChanged();
    Redraw();
  }

  protected override void OnPropertyChanged(string? propertyName = null)
  {
    base.OnPropertyChanged(propertyName);
    if (propertyName == nameof(Source))
      Redraw();
  }

  private void Redraw()
  {
#if ANDROID
    if (Handler?.PlatformView is Android.Views.View view)
      view.PostInvalidateDelayed(80);
#endif
  }
}
