namespace Notes.Services.Media;

public sealed class DefaultRecentMediaProvider : IRecentMediaProvider
{
  public bool IsSupported => false;

  public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);

  public Task<IReadOnlyList<RecentImage>> GetRecentImagesAsync(int limit, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<RecentImage>>(Array.Empty<RecentImage>());

  public Task<Stream> OpenAsync(RecentImage image, CancellationToken ct = default)
      => throw new NotSupportedException();

  public Task<Stream?> LoadThumbnailAsync(RecentImage image, int sizePx, CancellationToken ct = default)
      => Task.FromResult<Stream?>(null);
}
