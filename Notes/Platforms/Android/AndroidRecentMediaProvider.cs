using Android.Content;
using Android.Database;
using Android.Graphics;
using Android.Provider;
using Notes.Services;
using Notes.Services.Media;
using AndroidUri = Android.Net.Uri;
using AndroidSize = Android.Util.Size;

namespace Notes.Platforms.Android;

public sealed class AndroidRecentMediaProvider : IRecentMediaProvider
{
  public bool IsSupported => true;

  public async Task<bool> EnsurePermissionAsync()
  {
    var status = await Permissions.CheckStatusAsync<ReadGalleryImagesPermission>();
    if (status != PermissionStatus.Granted)
      status = await Permissions.RequestAsync<ReadGalleryImagesPermission>();
    return status == PermissionStatus.Granted;
  }

  public Task<IReadOnlyList<RecentImage>> GetRecentImagesAsync(int limit, CancellationToken ct = default)
      => Task.Run<IReadOnlyList<RecentImage>>(() =>
      {
        var items = new List<RecentImage>(limit);
        var resolver = global::Android.App.Application.Context.ContentResolver;
        var collection = MediaStore.Images.Media.ExternalContentUri;
        if (resolver == null || collection == null)
          return items;

        string[] projection =
        {
          IBaseColumns.Id,
          MediaStore.IMediaColumns.DisplayName,
          MediaStore.IMediaColumns.DateAdded,
        };

        ICursor? cursor = null;
        try
        {
          cursor = QueryNewestFirst(resolver, collection, projection, limit);
          if (cursor == null)
            return items;

          int idCol = cursor.GetColumnIndexOrThrow(IBaseColumns.Id);
          int nameCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DisplayName);
          int dateCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DateAdded);

          while (cursor.MoveToNext() && items.Count < limit)
          {
            ct.ThrowIfCancellationRequested();
            long id = cursor.GetLong(idCol);
            string name = cursor.IsNull(nameCol) ? $"image_{id}.jpg" : cursor.GetString(nameCol) ?? $"image_{id}.jpg";
            long dateSec = cursor.IsNull(dateCol) ? 0 : cursor.GetLong(dateCol);
            var uri = AndroidUri.WithAppendedPath(collection, id.ToString());
            if (uri == null)
              continue;
            items.Add(new RecentImage(id.ToString(), uri.ToString()!, name,
                DateTimeOffset.FromUnixTimeSeconds(dateSec)));
          }
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Java.Lang.Exception ex)
        {
          DebugLogService.Current?.Log($"recent-media: query failed - {ex.Message}");
        }
        finally
        {
          cursor?.Close();
        }

        return items;
      }, ct);

  public async Task<Stream> OpenAsync(RecentImage image, CancellationToken ct = default)
  {
    var resolver = global::Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("no content resolver");
    var uri = AndroidUri.Parse(image.Uri) ?? throw new IOException($"bad uri {image.Uri}");

    var input = await Task.Run(() => resolver.OpenInputStream(uri), ct);
    if (input == null)
      throw new IOException($"cannot open {image.Uri}");

    var buffer = new MemoryStream();
    try
    {
      await input.CopyToAsync(buffer, ct);
    }
    finally
    {
      input.Dispose();
    }
    buffer.Position = 0;
    return buffer;
  }

  public async Task<Stream?> LoadThumbnailAsync(RecentImage image, int sizePx, CancellationToken ct = default)
  {
    try
    {
      var resolver = global::Android.App.Application.Context.ContentResolver;
      if (resolver == null)
        return null;
      var uri = AndroidUri.Parse(image.Uri);
      if (uri == null)
        return null;

      Bitmap? bitmap = await Task.Run(() =>
      {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
          return resolver.LoadThumbnail(uri, new AndroidSize(sizePx, sizePx), null);

#pragma warning disable CA1422, CS0618
        return MediaStore.Images.Thumbnails.GetThumbnail(
            resolver, long.Parse(image.Id), ThumbnailKind.MiniKind, null);
#pragma warning restore CA1422, CS0618
      }, ct);

      if (bitmap == null)
        return null;

      var ms = new MemoryStream();
      bitmap.Compress(Bitmap.CompressFormat.Png!, 100, ms);
      bitmap.Recycle();
      ms.Position = 0;
      return ms;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      DebugLogService.Current?.Log($"recent-media: thumbnail failed - {ex.Message}");
      return null;
    }
  }

  private static ICursor? QueryNewestFirst(ContentResolver resolver, AndroidUri collection, string[] projection, int limit)
  {
    string dateDesc = $"{MediaStore.IMediaColumns.DateAdded} DESC";
    try
    {
      return resolver.Query(collection, projection, null, null, $"{dateDesc} LIMIT {limit}");
    }
    catch (Java.Lang.Exception)
    {
      return resolver.Query(collection, projection, null, null, dateDesc);
    }
  }
}

public sealed class ReadGalleryImagesPermission : Permissions.BasePlatformPermission
{
  public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
      OperatingSystem.IsAndroidVersionAtLeast(33)
          ? new[] { (global::Android.Manifest.Permission.ReadMediaImages, true) }
          : new[] { (global::Android.Manifest.Permission.ReadExternalStorage, true) };
}
