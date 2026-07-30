using System.Text;
using System.Text.RegularExpressions;
using Notes.Models;

namespace Notes.Helpers;

public static class ItemInfoHelper
{
  private static readonly Regex MarkdownLinkRx =
      new(@"\[[^\]]*\]\((https?://[^)\s]+)\)", RegexOptions.Compiled);
  private static readonly Regex BareUrlRx =
      new(@"(?<![(\""])https?://[^\s)""']+", RegexOptions.Compiled);
  private static readonly Regex ImageRefRx =
      new(@"!\[[^\]]*\]\([^)\s]+\)", RegexOptions.Compiled);

  public static int CountImages(string? content)
  {
    if (string.IsNullOrEmpty(content)) return 0;
    return ImageRefRx.Matches(content).Count;
  }

  public static IReadOnlyList<string> ExtractLinks(string? content)
  {
    if (string.IsNullOrEmpty(content)) return Array.Empty<string>();

    var links = new List<string>();
    foreach (Match m in MarkdownLinkRx.Matches(content))
      links.Add(m.Groups[1].Value);

    // strip markdown links out first so their URL isn't also matched as bare
    var withoutMarkdownLinks = MarkdownLinkRx.Replace(content, "");
    foreach (Match m in BareUrlRx.Matches(withoutMarkdownLinks))
      links.Add(m.Value);

    return links.Distinct().ToList();
  }

  public static string FormatSize(long bytes)
  {
    string[] units = { "B", "KB", "MB", "GB" };
    double size = bytes;
    int unit = 0;
    while (size >= 1024 && unit < units.Length - 1)
    {
      size /= 1024;
      unit++;
    }
    return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.0} {units[unit]}";
  }

  public static string BuildNoteInfo(Note note)
  {
    var bytes = Encoding.UTF8.GetByteCount(note.Content ?? "");
    var wordCount = string.IsNullOrWhiteSpace(note.Content)
        ? 0
        : note.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    var links = ExtractLinks(note.Content);
    var imageCount = CountImages(note.Content);

    var sb = new StringBuilder();
    sb.AppendLine($"created: {note.Created:g}");
    sb.AppendLine($"modified: {note.Modified:g}");
    sb.AppendLine($"size: {FormatSize(bytes)} ({wordCount} words)");
    sb.AppendLine($"images: {imageCount}");
    if (note.Tags.Count > 0)
      sb.AppendLine($"tags: {string.Join(", ", note.Tags)}");
    AppendLinks(sb, links);
    return sb.ToString().TrimEnd();
  }

  public static string BuildFolderInfo(Folder folder, IReadOnlyList<Note> notes)
  {
    var totalBytes = notes.Sum(n => Encoding.UTF8.GetByteCount(n.Content ?? ""));
    var totalImages = notes.Sum(n => CountImages(n.Content));
    var links = notes.SelectMany(n => ExtractLinks(n.Content)).Distinct().ToList();

    var sb = new StringBuilder();
    sb.AppendLine($"modified: {folder.Modified:g}");
    sb.AppendLine($"notes: {notes.Count}");
    sb.AppendLine($"total size: {FormatSize(totalBytes)}");
    sb.AppendLine($"images: {totalImages}");
    AppendLinks(sb, links);
    return sb.ToString().TrimEnd();
  }

  public static string BuildOverallInfo(IReadOnlyList<Folder> folders, IReadOnlyList<Note> notes)
  {
    var totalBytes = notes.Sum(n => Encoding.UTF8.GetByteCount(n.Content ?? ""));
    var totalImages = notes.Sum(n => CountImages(n.Content));
    var links = notes.SelectMany(n => ExtractLinks(n.Content)).Distinct().ToList();
    var lastModified = notes.Count == 0
        ? (DateTime?)null
        : notes.Max(n => n.Modified);

    var sb = new StringBuilder();
    sb.AppendLine($"folders: {folders.Count}");
    sb.AppendLine($"notes: {notes.Count}");
    if (lastModified.HasValue)
      sb.AppendLine($"last modified: {lastModified.Value:g}");
    sb.AppendLine($"total size: {FormatSize(totalBytes)}");
    sb.AppendLine($"images: {totalImages}");
    AppendLinks(sb, links);
    return sb.ToString().TrimEnd();
  }

  private static void AppendLinks(StringBuilder sb, IReadOnlyList<string> links)
  {
    if (links.Count == 0)
    {
      sb.AppendLine("links: none");
      return;
    }
    sb.AppendLine($"links ({links.Count}):");
    foreach (var link in links)
      sb.AppendLine(link);
  }
}
