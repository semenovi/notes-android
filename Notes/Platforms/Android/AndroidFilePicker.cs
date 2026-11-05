using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Notes.Helpers;

namespace Notes.Platforms.Android;

public static class AndroidFilePicker
{
  private const int RequestCode = 7319;
  private static TaskCompletionSource<IReadOnlyList<FileResult>?>? _pending;

  public static Task<IReadOnlyList<FileResult>?> PickMultipleAsync(string[] mimeTypes, string title)
      => PickAsync(mimeTypes, title, true);

  public static async Task<FileResult?> PickSingleAsync(string[] mimeTypes, string title)
  {
    var files = await PickAsync(mimeTypes, title, false);
    return files?.FirstOrDefault();
  }

  private static Task<IReadOnlyList<FileResult>?> PickAsync(string[] mimeTypes, string title, bool multiple)
  {
    var activity = Platform.CurrentActivity;
    if (activity == null)
      return Task.FromResult<IReadOnlyList<FileResult>?>(null);

    _pending = new TaskCompletionSource<IReadOnlyList<FileResult>?>();
    try
    {
      var intent = new Intent(Intent.ActionOpenDocument);
      intent.AddCategory(Intent.CategoryOpenable);
      intent.SetType(mimeTypes.Length == 1 ? mimeTypes[0] : "*/*");
      if (mimeTypes.Length > 1)
        intent.PutExtra(Intent.ExtraMimeTypes, mimeTypes);
      if (multiple)
        intent.PutExtra(Intent.ExtraAllowMultiple, true);

      var downloads = global::Android.Net.Uri.Parse(
          "content://com.android.providers.downloads.documents/root/downloads");
      intent.PutExtra("android.provider.extra.INITIAL_URI", downloads);

      activity.StartActivityForResult(intent, RequestCode);
    }
    catch
    {
      _pending.TrySetResult(null);
      _pending = null;
    }

    return _pending.Task;
  }

  public static void HandleResult(int requestCode, Result resultCode, Intent? data)
  {
    if (requestCode != RequestCode)
      return;

    var pending = _pending;
    _pending = null;
    if (pending == null)
      return;

    if (resultCode != Result.Ok || data == null)
    {
      pending.TrySetResult(null);
      return;
    }
    _ = CopyResultsAsync(pending, data);
  }

  private static async Task CopyResultsAsync(
      TaskCompletionSource<IReadOnlyList<FileResult>?> pending,
      Intent data)
  {
    var results = new List<FileResult>();
    try
    {
      var uris = new List<global::Android.Net.Uri>();
      if (data.ClipData != null)
      {
        for (int i = 0; i < data.ClipData.ItemCount; i++)
        {
          var uri = data.ClipData.GetItemAt(i)?.Uri;
          if (uri != null)
            uris.Add(uri);
        }
      }
      else if (data.Data != null)
      {
        uris.Add(data.Data);
      }

      var activity = Platform.CurrentActivity;
      var resolver = activity?.ContentResolver;
      if (resolver == null)
      {
        pending.TrySetResult(null);
        return;
      }

      foreach (var uri in uris)
      {
        string name = GetDisplayName(resolver, uri) ?? $"picked_{Guid.NewGuid():N}";
        string safeName = Path.GetFileName(name);
        string pickedDir = Path.Combine(AppPaths.CacheRoot, "picked");
        Directory.CreateDirectory(pickedDir);
        string path = Path.Combine(pickedDir, $"{Guid.NewGuid():N}_{safeName}");
        using var input = resolver.OpenInputStream(uri);
        if (input == null)
          continue;
        using var output = File.Create(path);
        await input.CopyToAsync(output);
        results.Add(new FileResult(path, safeName));
      }
    }
    catch
    {
      pending.TrySetResult(null);
      return;
    }
    pending.TrySetResult(results.Count == 0 ? null : results);
  }

  private static string? GetDisplayName(ContentResolver resolver, global::Android.Net.Uri uri)
  {
    try
    {
      using var cursor = resolver.Query(uri, new[] { OpenableColumns.DisplayName }, null, null, null);
      if (cursor != null && cursor.MoveToFirst())
      {
        int index = cursor.GetColumnIndex(OpenableColumns.DisplayName);
        if (index >= 0)
          return cursor.GetString(index);
      }
    }
    catch
    {
    }
    return null;
  }
}
