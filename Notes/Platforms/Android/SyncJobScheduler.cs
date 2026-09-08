using Android.App.Job;
using Android.Content;
using Notes.Services;

namespace Notes;

// Registers the OS-scheduled background sync jobs. Replaces the old persistent
// dataSync foreground service, which Android 15+ caps at ~6h/24h and forbids starting
// from the background - the source of the repeated ForegroundService* crashes.
public static class SyncJobScheduler
{
  public const int PeriodicJobId = 7001;
  public const int OneShotJobId = 7002;

  // 15 min is the JobScheduler floor for a periodic job.
  private static readonly long PeriodicIntervalMs = (long)TimeSpan.FromMinutes(15).TotalMilliseconds;
  private static readonly long OneShotDeadlineMs = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

  private static JobScheduler? Scheduler
      => Android.App.Application.Context.GetSystemService(Context.JobSchedulerService) as JobScheduler;

  private static ComponentName Component
      => new(Android.App.Application.Context, Java.Lang.Class.FromType(typeof(SyncJobService)));

  // Idempotent: keeps the existing periodic job (and its OS interval timer) if one is
  // already registered, so calling this on every app start/stop doesn't reset the clock.
  public static void EnsurePeriodic()
  {
    var scheduler = Scheduler;
    if (scheduler == null) return;

    if (OperatingSystem.IsAndroidVersionAtLeast(24) && scheduler.GetPendingJob(PeriodicJobId) != null)
      return;

    try
    {
      var job = new JobInfo.Builder(PeriodicJobId, Component)
          .SetRequiredNetworkType(NetworkType.Any)
          .SetPersisted(true)
          .SetPeriodic(PeriodicIntervalMs)
          .Build();
      scheduler.Schedule(job);
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"bg-job-periodic-err: {ex.Message}");
    }
  }

  // Fired when the app is backgrounded so edits made right before locking the phone
  // sync within seconds. Expedited where available; otherwise a plain immediate job.
  public static void ScheduleOneShot()
  {
    var scheduler = Scheduler;
    if (scheduler == null) return;

    try
    {
      var builder = new JobInfo.Builder(OneShotJobId, Component)
          .SetRequiredNetworkType(NetworkType.Any)
          .SetPersisted(false);

      if (OperatingSystem.IsAndroidVersionAtLeast(31))
        builder.SetExpedited(true);
      else
        builder.SetMinimumLatency(0).SetOverrideDeadline(OneShotDeadlineMs);

      scheduler.Schedule(builder.Build());
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"bg-job-oneshot-err: {ex.Message}");
    }
  }

  public static void CancelAll()
  {
    var scheduler = Scheduler;
    if (scheduler == null) return;
    scheduler.Cancel(PeriodicJobId);
    scheduler.Cancel(OneShotJobId);
  }
}
