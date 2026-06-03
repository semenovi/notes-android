using Notes.Data.Storage;
using Notes.Services;
using Notes.Services.Notes;
using Notes.Services.Sync;
using Notes.Views.Windows;

namespace Notes;

public partial class App : Application
{
  private readonly ReactiveSyncService _reactiveSync;
  private readonly MediaStorage _mediaStorage;
  private readonly DebugLogService _debugLog;

  public App(ReactiveSyncService reactiveSync, MediaStorage mediaStorage, DebugLogService debugLog)
  {
    _reactiveSync = reactiveSync;
    _mediaStorage = mediaStorage;
    _debugLog = debugLog;
    InitializeComponent();

#if !WINDOWS
    MainPage = new AppShell();
#endif
  }

  protected override void OnStart()
  {
    base.OnStart();
#if WINDOWS
    _debugLog.StartFileLogging(Path.Combine(AppContext.BaseDirectory, "notes_debug.log"));
#endif
    _ = _reactiveSync.StartAsync();
    _ = _mediaStorage.MigrateExistingMediaAsync();
  }

  protected override void OnSleep()
  {
    base.OnSleep();
    _ = _reactiveSync.StopAsync();
  }

  protected override void OnResume()
  {
    base.OnResume();
    _ = _reactiveSync.StartAsync();
  }

#if WINDOWS
  protected override void OnHandlerChanged()
  {
    base.OnHandlerChanged();
    if (Handler?.MauiContext != null && MainPage == null)
    {
      MainPage = new MainWindow();
    }
  }
#endif

  protected override Window CreateWindow(IActivationState activationState)
  {
    var window = base.CreateWindow(activationState);

#if WINDOWS
        window.Title = "Notes";
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