using Notes.Services.Media;

namespace Notes.Views.Controls;

public enum AttachmentSource
{
  Gallery,
  Files,
  Camera,
}

public abstract record AttachmentSheetResult;

public sealed record AttachmentDismissed : AttachmentSheetResult;

public sealed record AttachmentSourceChosen(AttachmentSource Source) : AttachmentSheetResult;

public sealed record AttachmentRecentsChosen(IReadOnlyList<RecentImage> Items) : AttachmentSheetResult;
