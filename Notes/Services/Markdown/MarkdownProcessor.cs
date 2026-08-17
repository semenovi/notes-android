using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace Notes.Services.Markdown;

public class MarkdownProcessor
{
  // Bounds how long a single missing image can hold up note rendering before falling
  // back to a blank placeholder — the fetch itself keeps running and the image resolves
  // next time the note is opened (or immediately, if InjectImagesIntoWebViewAsync is
  // still in progress and reaches it again via a later call).
  private static readonly TimeSpan OnDemandFetchTimeout = TimeSpan.FromSeconds(20);

  private readonly List<ISyntaxExtension> _extensions = new List<ISyntaxExtension>();
  private readonly Services.Notes.MediaManager _mediaManager;
  private readonly Services.Sync.MediaDownloadCoordinator _mediaDownloadCoordinator;
  private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _dataUriCache = new();

  public MarkdownProcessor(Services.Notes.MediaManager mediaManager,
      Services.Sync.MediaDownloadCoordinator mediaDownloadCoordinator)
  {
    _mediaManager = mediaManager;
    _mediaDownloadCoordinator = mediaDownloadCoordinator;
    _mediaManager.MediaAdded += id => _dataUriCache.TryRemove(id, out _);
    _mediaManager.MediaDeleted += id => _dataUriCache.TryRemove(id, out _);
  }

  public void InvalidateMediaCache(string mediaId) => _dataUriCache.TryRemove(mediaId, out _);

  public bool TryGetCachedDataUri(string mediaId, out string? dataUri) =>
      _dataUriCache.TryGetValue(mediaId, out dataUri!);

  public void RegisterExtension(ISyntaxExtension extension)
  {
    if (!_extensions.Any(e => e.Name == extension.Name))
    {
      _extensions.Add(extension);
    }
  }

  public async Task<string> ConvertToHtmlAsync(string markdown)
  {
    string processed = markdown;

    foreach (var extension in _extensions)
    {
      processed = extension.Process(processed);
    }

    processed = await ProcessMediaLinksAsync(processed);
    string preRenderText = processed;
    processed = ProcessBasicMarkdown(processed);
    processed = TagSourceLines(preRenderText, processed);

    return processed;
  }

  // stamps each rendered block with the source markdown line it came from
  // (data-src-line), so the WebView preview's scroll position can be mapped to the
  // corresponding line in the plain-text editor. Relies on ProcessBasicMarkdown
  // emitting exactly one block element per non-blank source line (fenced code blocks
  // being the one exception, collapsed into a single <pre>)
  private static readonly System.Text.RegularExpressions.Regex BlockTagRegex = new(
      @"<(?:p|h1|h2|h3|li|pre|hr|div class=""task-item"")>",
      System.Text.RegularExpressions.RegexOptions.Compiled);

  private static string TagSourceLines(string sourceText, string html)
  {
    var lineNumbers = ComputeBlockSourceLines(sourceText);
    if (lineNumbers.Count == 0)
      return html;

    int index = 0;
    return BlockTagRegex.Replace(html, m =>
    {
      if (index >= lineNumbers.Count)
        return m.Value;

      int line = lineNumbers[index++];
      int insertAt = m.Value.IndexOf('>');
      return m.Value.Insert(insertAt, $" data-src-line=\"{line}\"");
    });
  }

  private static List<int> ComputeBlockSourceLines(string sourceText)
  {
    var normalized = sourceText.Replace("\r\n", "\n").Replace("\r", "\n");
    var lines = normalized.Split('\n');
    var result = new List<int>();

    for (int i = 0; i < lines.Length; i++)
    {
      if (lines[i].Length == 0)
        continue;

      if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^```[^\n`]*$"))
      {
        int close = -1;
        for (int j = i + 1; j < lines.Length; j++)
        {
          if (lines[j] == "```") { close = j; break; }
        }
        if (close >= 0)
        {
          result.Add(i);
          i = close;
          continue;
        }
      }

      result.Add(i);
    }

    return result;
  }

  public Task<string> ProcessMediaLinksAsync(string markdown)
  {
    var result = System.Text.RegularExpressions.Regex.Replace(
      markdown,
      @"!\[(.*?)\]\(media:(.*?)\)",
      m => $"<img id=\"media-{m.Groups[2].Value}\" data-media-id=\"{m.Groups[2].Value}\" alt=\"{m.Groups[1].Value}\" class=\"media-lazy\">");
    return Task.FromResult(result);
  }

  public async Task InjectImagesIntoWebViewAsync(string markdown, WebView webView,
      Action<int, int>? onProgress = null)
  {
    var mediaIds = new System.Text.RegularExpressions.Regex(@"!\[.*?\]\(media:(.*?)\)")
      .Matches(markdown)
      .Cast<System.Text.RegularExpressions.Match>()
      .Select(m => m.Groups[1].Value)
      .Distinct()
      .ToList();

    if (mediaIds.Count == 0)
      return;

    int total = mediaIds.Count;

    // Load and inject images one at a time: limits peak memory to a single image
    // and lets GC run between items. Concurrent loading via Task.WhenAll caused
    // OOM on Android when notes contain many photos, silently dropping the last half.
    for (int i = 0; i < mediaIds.Count; i++)
    {
      var id = mediaIds[i];
      try
      {
        // Metadata and content arrive together from the server, so a media item that
        // hasn't been fetched yet has neither — GetMediaAsync alone can't tell "still
        // downloading" apart from "doesn't exist". Ask the coordinator first; it no-ops
        // if the content is already local.
        var item = await _mediaManager.GetMediaAsync(id);
        if (item == null)
        {
          if (!await EnsureLocalAsync(id)) continue;
          item = await _mediaManager.GetMediaAsync(id);
          if (item == null) continue;
        }
        string dataUri = await GetMediaDataUriAsync(id, item);
        if (string.IsNullOrEmpty(dataUri)) continue;
        string js = $"(function(){{var e=document.getElementById('media-{id}');if(e)e.src='{dataUri}';}})();";
        await MainThread.InvokeOnMainThreadAsync(() => webView.EvaluateJavaScriptAsync(js));
        onProgress?.Invoke(i + 1, total);
      }
      catch { }
    }
  }

  // Waits up to OnDemandFetchTimeout for the coordinator to fetch mediaId if it isn't
  // local yet. Returns immediately (true) if it's already there.
  private async Task<bool> EnsureLocalAsync(string mediaId)
  {
    using var cts = new CancellationTokenSource(OnDemandFetchTimeout);
    return await _mediaDownloadCoordinator.EnsureAvailableAsync(mediaId, cts.Token);
  }

  public async Task<string> GetFullResDataUriAsync(string mediaId)
  {
    try
    {
      await EnsureLocalAsync(mediaId);
      var item = await _mediaManager.GetMediaAsync(mediaId);
      if (item == null) return "";
      string fileType = item.FileType?.ToLowerInvariant() ?? "png";
      byte[] bytes = await _mediaManager.GetRawContentAsync(mediaId);
      string mimeType = fileType switch
      {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "image/png",
      };
      return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
    catch
    {
      return "";
    }
  }

  private async Task<string> GetMediaDataUriAsync(string mediaId, Models.MediaItem? mediaItem = null)
  {
    if (_dataUriCache.TryGetValue(mediaId, out var cached))
      return cached;

    try
    {
      await EnsureLocalAsync(mediaId);
      mediaItem ??= await _mediaManager.GetMediaAsync(mediaId);
      string fileType = mediaItem?.FileType?.ToLowerInvariant() ?? "png";

      using var stream = await _mediaManager.GetMediaContentAsync(mediaId);
      using var ms = new MemoryStream();
      await stream.CopyToAsync(ms);
      byte[] bytes = ms.ToArray();

      bool isRasterImage = fileType is "jpg" or "jpeg" or "png" or "webp";
      if (isRasterImage)
        bytes = await Task.Run(() => ResizeImageForDisplay(bytes));

      string mimeType = fileType switch
      {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => "image/png",
      };

      string dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
      _dataUriCache[mediaId] = dataUri;
      return dataUri;
    }
    catch
    {
      return "";
    }
  }

  private static byte[] ResizeImageForDisplay(byte[] data)
  {
    const int MaxDim = 1200;
    try
    {
      using var inMs = new MemoryStream(data);
      var img = PlatformImage.FromStream(inMs);
      if (img.Width <= MaxDim && img.Height <= MaxDim)
        return data;
      float scale = Math.Min((float)MaxDim / img.Width, (float)MaxDim / img.Height);
      var resized = img.Resize((int)(img.Width * scale), (int)(img.Height * scale), ResizeMode.Fit);
      using var outMs = new MemoryStream();
      resized.Save(outMs);
      return outMs.ToArray();
    }
    catch
    {
      return data;
    }
  }

  private string ProcessBasicMarkdown(string markdown)
  {
    var processedText = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

    // Fenced code blocks are pulled out before any other rule runs, so bold/italic/list/
    // paragraph regexes below (which operate per-line) can't reach into their contents
    // and split them into extra <p> lines.
    var codeBlocks = new List<string>();
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^```([^\n`]*)\n([\s\S]*?)^```$",
        m =>
        {
            var lang = m.Groups[1].Value.Trim();
            var code = m.Groups[2].Value.TrimEnd('\n');
            var langClass = string.IsNullOrEmpty(lang) ? "" : $" class=\"language-{lang}\"";
            codeBlocks.Add($"<pre><code{langClass}>{code}</code></pre>");
            return $"CODEBLOCK{codeBlocks.Count - 1}";
        },
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    // Task lines must be consumed before the generic "- " list rule below.
    // data-task-index maps back to the Nth TaskLineRegex match in the source
    // markdown — TaskListMarkdown.Toggle relies on that ordering.
    int taskIndex = 0;
    processedText = TaskListMarkdown.TaskLineRegex.Replace(processedText, m =>
    {
      bool done = m.Groups[1].Value != " ";
      string check = done ? " checked" : "";
      string doneClass = done ? " done" : "";
      return $"<div class=\"task-item\"><input type=\"checkbox\" class=\"task-checkbox\" data-task-index=\"{taskIndex++}\"{check}><span class=\"task-text{doneClass}\">{m.Groups[2].Value}</span></div>";
    });

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^(-{3,}|\*{3,}|_{3,})$",
        "<hr>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^# (.+)$",
        "<h1>$1</h1>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^## (.+)$",
        "<h2>$1</h2>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^### (.+)$",
        "<h3>$1</h3>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"\*\*(.*?)\*\*",
        "<strong>$1</strong>"
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"\*(.*?)\*",
        "<em>$1</em>"
    );

    // Regular images (non-media: scheme, already resolved to src url)
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"!\[(.*?)\]\(((?!media:)[^)]+)\)",
        "<img src=\"$2\" alt=\"$1\" style=\"max-width:100%;border-radius:4px;\" />"
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"(?<!\!)\[(.*?)\]\((.*?)\)",
        "<a href=\"$2\">$1</a>"
    );

    // List items are tagged with their source type first, then grouped into
    // <ul>/<ol> in a single pass below. Grouping pairwise (old approach) couldn't
    // merge runs longer than two items: .NET regex matches don't overlap, so each
    // merge consumed the next item's opening <li>, leaving it for a fresh <ul>/<ol>.
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^- (.+)$",
        "<li data-list=\"ul\">$1</li>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^(\d+)\. (.+)$",
        "<li data-list=\"ol\">$2</li>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^<li data-list=""(ul|ol)"">.*</li>$(?:\n^<li data-list=""\1"">.*</li>$)*",
        m =>
        {
            var tag = m.Groups[1].Value;
            var items = m.Value.Replace($" data-list=\"{tag}\"", "");
            return $"<{tag}>{items}</{tag}>";
        },
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"`([^`]+)`",
        "<code>$1</code>"
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^(?!<[a-z]+>)(.+?)$",
        "<p>$1</p>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    for (int i = 0; i < codeBlocks.Count; i++)
    {
      processedText = processedText.Replace($"CODEBLOCK{i}", codeBlocks[i]);
    }

    processedText = processedText.Replace("<p><h", "<h");
    processedText = processedText.Replace("</h1></p>", "</h1>");
    processedText = processedText.Replace("</h2></p>", "</h2>");
    processedText = processedText.Replace("</h3></p>", "</h3>");
    processedText = processedText.Replace("<p><pre>", "<pre>");
    processedText = processedText.Replace("</pre></p>", "</pre>");
    processedText = processedText.Replace("<p><div class=\"task-item\">", "<div class=\"task-item\">");
    processedText = processedText.Replace("</div></p>", "</div>");
    processedText = processedText.Replace("<p><ul>", "<ul>");
    processedText = processedText.Replace("</ul></p>", "</ul>");
    processedText = processedText.Replace("<p><ol>", "<ol>");
    processedText = processedText.Replace("</ol></p>", "</ol>");
    processedText = processedText.Replace("<p></p>", "");
    processedText = processedText.Replace("<p><hr></p>", "<hr>");
    processedText = processedText.Replace("<p><hr>", "<hr>");
    processedText = processedText.Replace("<hr></p>", "<hr>");

    return processedText;
  }
}