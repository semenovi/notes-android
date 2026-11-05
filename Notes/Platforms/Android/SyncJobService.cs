using Android.App;
using Android.App.Job;
using Notes.Services;
using Notes.Services.Sync;

namespace Notes;

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
    return true; 
  }

  public override bool OnStopJob(JobParameters? parameters)
  {
    _cts?.Cancel();
    return true; 
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
      catch {  }
    }
  }
}
