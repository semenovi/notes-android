namespace Notes.Services.Media;

public sealed record RecentImage(string Id, string Uri, string DisplayName, DateTimeOffset TakenAt);

public interface IRecentMediaProvider
{
  bool IsSupported { get; }

  Task<bool> EnsurePermissionAsync();

  Task<IReadOnlyList<RecentImage>> GetRecentImagesAsync(int limit, CancellationToken ct = default);

  Task<Stream> OpenAsync(RecentImage image, CancellationToken ct = default);

  Task<Stream?> LoadThumbnailAsync(RecentImage image, int sizePx, CancellationToken ct = default);
}
