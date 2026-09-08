using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Notes.Services;

namespace Notes.Services.Sync;

// Single background-sync entry point shared by the platform schedulers (the Android
// JobService today). Runs one full sync plus a bounded media drain, resolving its
// dependencies from the running MAUI app's service container rather than taking them
// by constructor - the scheduler that invokes it has no DI of its own.
public static class BackgroundSyncRunner
{
  private static readonly TimeSpan SyncTimeout = TimeSpan.FromMinutes(2);
  private static readonly TimeSpan MediaDrainBudget = TimeSpan.FromMinutes(2);

  // Returns true when the run completed (including "sync disabled, nothing to do"),
  // false on failure/timeout so the caller can ask the OS to reschedule.
  public static async Task<bool> RunOnceAsync(CancellationToken ct)
  {
    IServiceProvider? services = await WaitForServicesAsync(ct);
    if (services == null)
    {
      DebugLogService.Current?.Log("bg-sync-abort: no service provider");
      return false;
    }

    var settingsService = services.GetService<SyncSettingsService>();
    var syncManager = services.GetService<SyncManager>();
    var reactive = services.GetService<ReactiveSyncService>();
    var mediaCoordinator = services.GetService<MediaDownloadCoordinator>();
    if (settingsService == null || syncManager == null || mediaCoordinator == null)
    {
      DebugLogService.Current?.Log("bg-sync-abort: services missing");
      return false;
    }

    var settings = await settingsService.LoadAsync();
    if (!settings.Enabled
        || string.IsNullOrEmpty(settings.ServerUrl)
        || string.IsNullOrEmpty(settings.ApiToken))
    {
      DebugLogService.Current?.Log("bg-sync-skip: disabled/unconfigured");
      return true;
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(SyncTimeout);
    try
    {
      DebugLogService.Current?.Log("bg-sync-start");
      int applied = await syncManager.SynchronizeAsync(
          ReactiveSyncService.DefaultProfile, null, timeoutCts.Token);
      DebugLogService.Current?.Log($"bg-sync-done applied={applied}");

      // When the app is foreground the coordinator's own loop is already draining the
      // queue - don't run a second drain over the same items.
      if (reactive is not { IsRunning: true })
      {
        DebugLogService.Current?.Log($"bg-sync-media-drain pending={mediaCoordinator.PendingCount}");
        await mediaCoordinator.DrainPendingAsync(
            settings.ServerUrl, settings.ApiToken, MediaDrainBudget, ct);
      }
      return true;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      DebugLogService.Current?.Log("bg-sync-timeout");
      return false;
    }
    catch (OperationCanceledException)
    {
      DebugLogService.Current?.Log("bg-sync-cancelled");
      return false;
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"bg-sync-err: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
  }

  // A job may start the process cold, before MauiApplication has finished building the
  // app. Give the container a few seconds to appear before giving up.
  private static async Task<IServiceProvider?> WaitForServicesAsync(CancellationToken ct)
  {
    for (int i = 0; i < 20; i++)
    {
      var sp = IPlatformApplication.Current?.Services;
      if (sp != null) return sp;
      try { await Task.Delay(250, ct); }
      catch (OperationCanceledException) { return null; }
    }
    return IPlatformApplication.Current?.Services;
  }
}
