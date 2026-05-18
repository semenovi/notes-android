using Notes.Services.Notes;
using Notes.Views.Windows;

namespace Notes;

public partial class App : Application
{
  public App()
  {
    InitializeComponent();

#if !WINDOWS
    MainPage = new AppShell();
#endif
  }

#if WINDOWS
  protected override void OnHandlerChanged()
  {
    base.OnHandlerChanged();
    if (Handler?.MauiContext != null && MainPage == null)
    {
      var services = Handler.MauiContext.Services;
      MainPage = new MainWindow(
          services.GetRequiredService<FolderManager>(),
          services.GetRequiredService<NoteManager>());
    }
  }
#endif

  protected override Window CreateWindow(IActivationState activationState)
  {
    var window = base.CreateWindow(activationState);

#if WINDOWS
        window.Title = "Notes - Записная книжка";
        window.MinimumWidth = 800;
        window.MinimumHeight = 600;
        window.Width = 1200;
        window.Height = 800;
#else
    window.MinimumWidth = 320;
    window.MinimumHeight = 500;
#endif

    return window;
  }
}