using Notes.Services;
using Notes.Services.Sync;
using Notes.Views.Windows;

namespace Notes;

public partial class App : Application
{
  private readonly ReactiveSyncService _reactiveSync;
  private readonly DebugLogService _debugLog;

  public App(ReactiveSyncService reactiveSync, DebugLogService debugLog)
  {
    _reactiveSync = reactiveSync;
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
  }

  protected override void OnSleep()
  {
    base.OnSleep();
#if ANDROID
    if (_reactiveSync.IsRunning)
      StartAndroidSyncService();
#else
    _ = _reactiveSync.StopAsync();
#endif
  }

  protected override void OnResume()
  {
    base.OnResume();
#if ANDROID
    StopAndroidSyncService();
#endif
    _ = _reactiveSync.StartAsync();
  }

#if ANDROID
  private static void StartAndroidSyncService()
  {
    var ctx = Android.App.Application.Context;
    var intent = new Android.Content.Intent(ctx, typeof(SyncForegroundService));
    if (OperatingSystem.IsAndroidVersionAtLeast(26))
      ctx.StartForegroundService(intent);
    else
      ctx.StartService(intent);
  }

  private static void StopAndroidSyncService()
  {
    var ctx = Android.App.Application.Context;
    ctx.StopService(new Android.Content.Intent(ctx, typeof(SyncForegroundService)));
  }
#endif

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