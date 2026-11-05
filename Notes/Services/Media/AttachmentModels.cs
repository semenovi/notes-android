namespace Notes.Services.Media;

public enum AttachmentKind
{
  Image,
  Pdf,
}

public sealed class PendingAttachment
{
  public required string FileName { get; init; }
  public required AttachmentKind Kind { get; init; }
  public required Func<CancellationToken, Task<Stream>> OpenAsync { get; init; }

  public IReadOnlyList<IImageEdit>? Edits { get; set; }
}

public interface IImageEdit
{
  Task<Stream> ApplyAsync(Stream source, CancellationToken ct);
}
