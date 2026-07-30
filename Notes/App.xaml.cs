using Notes.Services;
using Notes.Services.Sync;
using Notes.Views.Windows;
#if ANDROID
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platform;
#endif

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
    // OnStart and OnResume both fire on cold start (OnResume always follows OnStart) —
    // calling StartAsync unconditionally here raced a second RunPeriodicSyncAsync loop
    // against the one OnStart kicked off. StopAsync cancels the old loop but can't
    // actually interrupt an in-flight SynchronizeAsync call (no token threaded through
    // it), so the second loop's own sync blocked on SyncManager's lock the whole time —
    // its "syncing" progress session was shown (same priority, added later wins the
    // tie-break in ProgressNotificationService.Refresh) but never got a single Report()
    // call, permanently freezing the notification on the indeterminate spinner while the
    // real sync kept progressing underneath, invisible.
    if (!_reactiveSync.IsRunning)
      _ = _reactiveSync.StartAsync();
#else
    // On Windows OnStart and OnResume both fire on cold start — don't restart a
    // running service (OnSleep stops it, so a real resume still restarts here).
    if (!_reactiveSync.IsRunning)
      _ = _reactiveSync.StartAsync();
#endif
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

  // Toast/progress used to live inside each page's own RootGrid, which is what the
  // custom swipe-back gesture (see SwipeBackGesture/RootGrid.TranslationX in the
  // page code-behinds) animates. That dragged notifications along with the swipe and
  // spawned a fresh instance per page. Mounting one overlay directly on the activity's
  // content view — a sibling of the Shell's native view, not a child of any page —
  // keeps it fixed and shared across the whole app.
  private void OnAndroidWindowCreated(object? sender, EventArgs e)
  {
    if (sender is not Window window) return;
    window.Created -= OnAndroidWindowCreated;

    var mauiContext = window.Handler?.MauiContext;
    var activity = Platform.CurrentActivity;
    if (mauiContext == null || activity == null) return;

    var toastService = mauiContext.Services.GetRequiredService<ToastService>();
    var progressService = mauiContext.Services.GetRequiredService<ProgressNotificationService>();

    var toast = new Notes.Views.Controls.ToastOverlay();
    var progress = new Notes.Views.Controls.ProgressOverlay();
    var host = new Grid { InputTransparent = true };
    host.Add(toast);
    host.Add(progress);

    toastService.ToastRequested += toast.ShowToast;
    progressService.ShowRequested += progress.ShowProgress;
    progressService.UpdateRequested += progress.UpdateProgress;
    progressService.HideRequested += progress.HideProgress;
    if (progressService.Current != null)
      progress.ShowProgress(progressService.Current);

    var nativeView = host.ToPlatform(mauiContext);
    activity.AddContentView(nativeView, new Android.Views.ViewGroup.LayoutParams(
        Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.MatchParent));
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
        window.Title = "notes";
        window.MinimumWidth = 800;
        window.MinimumHeight = 600;
        window.Width = 1200;
        window.Height = 800;
#else
    window.MinimumWidth = 320;
    window.MinimumHeight = 500;
#endif

#if ANDROID
    window.Created += OnAndroidWindowCreated;
#endif

    return window;
  }
}