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
  private readonly ProgressNotificationService _progressService;
  private readonly SyncSettingsService _settingsService;

  public App(ReactiveSyncService reactiveSync, DebugLogService debugLog,
      ProgressNotificationService progressService, SyncSettingsService settingsService)
  {
    _reactiveSync = reactiveSync;
    _debugLog = debugLog;
    _progressService = progressService;
    _settingsService = settingsService;
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
#if ANDROID
    _ = SyncAndroidBackgroundJobsAsync();
#endif
  }

  protected override void OnSleep()
  {
    base.OnSleep();
    // No viewer for an in-app overlay while backgrounded; every underlying operation
    // (sync, media download, note render) keeps running regardless. On return, fresh
    // sessions represent whatever is actually active then.
    _progressService.CancelAll();
    // Stop the in-process SSE/timer: a cached process gets frozen on Android and
    // suspended on Windows, so those tasks can't make progress anyway. Background sync
    // is handed to the OS scheduler on Android (immediate one-shot + periodic).
    _ = _reactiveSync.StopAsync();
#if ANDROID
    _ = OnAndroidSleepAsync();
#endif
  }

  protected override void OnResume()
  {
    base.OnResume();
    // OnStart and OnResume both fire on cold start (OnResume always follows OnStart).
    // StartAsync stops any prior run first, but can't interrupt an in-flight
    // SynchronizeAsync, so a blind restart here would stack a second sync loop behind
    // SyncManager's lock. The guard keeps the one OnStart already kicked off.
    if (!_reactiveSync.IsRunning)
      _ = _reactiveSync.StartAsync();
  }

#if ANDROID
  // Keeps the periodic background-sync job registered while sync is on, and clears it
  // when sync is off. Cheap and idempotent - safe to call on every start.
  private async Task SyncAndroidBackgroundJobsAsync()
  {
    try
    {
      var settings = await _settingsService.LoadAsync();
      if (settings.Enabled
          && !string.IsNullOrEmpty(settings.ServerUrl)
          && !string.IsNullOrEmpty(settings.ApiToken))
        SyncJobScheduler.EnsurePeriodic();
      else
        SyncJobScheduler.CancelAll();
    }
    catch { }
  }

  private async Task OnAndroidSleepAsync()
  {
    try
    {
      var settings = await _settingsService.LoadAsync();
      if (settings.Enabled
          && !string.IsNullOrEmpty(settings.ServerUrl)
          && !string.IsNullOrEmpty(settings.ApiToken))
      {
        SyncJobScheduler.ScheduleOneShot();
        SyncJobScheduler.EnsurePeriodic();
      }
      else
      {
        SyncJobScheduler.CancelAll();
      }
    }
    catch { }
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