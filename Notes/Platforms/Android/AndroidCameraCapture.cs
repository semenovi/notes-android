using Android.App;
using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Notes.Helpers;

namespace Notes.Platforms.Android;

public static class AndroidCameraCapture
{
  private const int RequestCode = 7321;
  private static TaskCompletionSource<FileResult?>? _pending;
  private static string? _pendingPath;

  public static Task<FileResult?> CaptureAsync(bool front)
  {
    var activity = Platform.CurrentActivity;
    if (activity == null)
      return Task.FromResult<FileResult?>(null);

    try
    {
      string dir = Path.Combine(AppPaths.CacheRoot, "camera");
      Directory.CreateDirectory(dir);
      string path = Path.Combine(dir, $"photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
      var file = new Java.IO.File(path);
      var uri = global::AndroidX.Core.Content.FileProvider.GetUriForFile(activity, $"{activity.PackageName}.fileprovider", file);

      var intent = new Intent(MediaStore.ActionImageCapture);
      intent.PutExtra(MediaStore.ExtraOutput, uri);
      intent.PutExtra("android.intent.extras.CAMERA_FACING", front ? 1 : 0);
      intent.AddFlags(ActivityFlags.GrantWriteUriPermission);

      _pendingPath = path;
      _pending = new TaskCompletionSource<FileResult?>();
      activity.StartActivityForResult(intent, RequestCode);
      return _pending.Task;
    }
    catch
    {
      _pending = null;
      _pendingPath = null;
      return Task.FromResult<FileResult?>(null);
    }
  }

  public static void HandleResult(int requestCode, Result resultCode, Intent? data)
  {
    if (requestCode != RequestCode)
      return;

    var pending = _pending;
    string? path = _pendingPath;
    _pending = null;
    _pendingPath = null;
    if (pending == null)
      return;

    if (resultCode != Result.Ok || string.IsNullOrEmpty(path) || !File.Exists(path))
    {
      pending.TrySetResult(null);
      return;
    }

    var info = new FileInfo(path);
    if (info.Length == 0)
    {
      pending.TrySetResult(null);
      return;
    }

    pending.TrySetResult(new FileResult(path, Path.GetFileName(path)));
  }
}
