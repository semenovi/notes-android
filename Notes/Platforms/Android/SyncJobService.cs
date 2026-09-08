using Android.App;
using Android.App.Job;
using Notes.Services;
using Notes.Services.Sync;

namespace Notes;

// Runs one background sync when the OS scheduler fires (see SyncJobScheduler). Does the
// actual work off the main thread and reports completion via JobFinished; asks for a
// reschedule only when the run failed, so a transient network/server issue is retried
// with backoff instead of waiting for the next periodic tick.
[Service(Name = "com.madbearing.notes.SyncJobService",
         Permission = "android.permission.BIND_JOB_SERVICE",
         Exported = false)]
public class SyncJobService : JobService
{
  private CancellationTokenSource? _cts;

  public override bool OnStartJob(JobParameters? parameters)
  {
    _cts = new CancellationTokenSource();
    _ = RunAsync(parameters, _cts.Token);
    return true; // work continues on a background thread
  }

  public override bool OnStopJob(JobParameters? parameters)
  {
    _cts?.Cancel();
    return true; // reschedule - the system reclaimed us before we finished
  }

  private async Task RunAsync(JobParameters? parameters, CancellationToken ct)
  {
    bool ok = false;
    try
    {
      ok = await BackgroundSyncRunner.RunOnceAsync(ct);
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"bg-job-err: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
      try { JobFinished(parameters, !ok); }
      catch { /* job already torn down */ }
    }
  }
}
