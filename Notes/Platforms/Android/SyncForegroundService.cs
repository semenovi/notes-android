using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Notes;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync, Exported = false)]
public class SyncForegroundService : Service
{
    private const string ChannelId = "notes_sync";
    private const int NotifId = 1001;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureNotificationChannel();

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Notes")
            .SetContentText("Синхронизация активна")
            .SetSmallIcon(Android.Resource.Drawable.IcMenuRotate)
            .SetOngoing(true)
            .Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotifId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotifId, notification);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void EnsureNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var mgr = GetSystemService(NotificationService) as NotificationManager;
        if (mgr?.GetNotificationChannel(ChannelId) != null) return;
        mgr?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Sync", NotificationImportance.Low));
    }
}
