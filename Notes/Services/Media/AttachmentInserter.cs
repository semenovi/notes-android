using Notes.Services;
using Notes.Services.Notes;

namespace Notes.Services.Media;

public sealed class AttachmentInserter
{
  private readonly MediaManager _media;
  private readonly ProgressNotificationService _progress;

  public AttachmentInserter(MediaManager media, ProgressNotificationService progress)
  {
    _media = media;
    _progress = progress;
  }

  public async Task<string?> BuildInsertTextAsync(IReadOnlyList<PendingAttachment> items, CancellationToken ct = default)
  {
    if (items.Count == 0)
      return null;

    int total = items.Count;
    using var session = _progress.Begin("adding attachments", delayMs: 0);

    var parts = new List<string>(total);
    for (int i = 0; i < total; i++)
    {
      ct.ThrowIfCancellationRequested();
      var item = items[i];
      session.Report((double)i / total, total > 1 ? $"{i + 1} of {total}" : null);

      Stream stream = await item.OpenAsync(ct);
      try
      {
        if (item.Edits is { Count: > 0 })
        {
          foreach (var edit in item.Edits)
          {
            var next = await edit.ApplyAsync(stream, ct);
            if (!ReferenceEquals(next, stream))
            {
              stream.Dispose();
              stream = next;
            }
          }
        }

        var mediaItem = await _media.AddMediaAsync(stream, item.FileName);
        parts.Add($"![{item.FileName}]({_media.GetMediaUrl(mediaItem.Id)})");
      }
      finally
      {
        stream.Dispose();
      }

      session.Report((double)(i + 1) / total, total > 1 ? $"{i + 1} of {total}" : null);
    }

    return string.Join("\n\n", parts);
  }
}
