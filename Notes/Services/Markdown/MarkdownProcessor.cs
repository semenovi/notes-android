using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace Notes.Services.Markdown;

public class MarkdownProcessor
{
  private readonly List<ISyntaxExtension> _extensions = new List<ISyntaxExtension>();
  private readonly Services.Notes.MediaManager _mediaManager;
  private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _dataUriCache = new();

  public MarkdownProcessor(Services.Notes.MediaManager mediaManager)
  {
    _mediaManager = mediaManager;
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
    processed = ProcessBasicMarkdown(processed);

    return processed;
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
        var item = await _mediaManager.GetMediaAsync(id);
        if (item == null) continue;
        string dataUri = await GetMediaDataUriAsync(id, item);
        if (string.IsNullOrEmpty(dataUri)) continue;
        string js = $"(function(){{var e=document.getElementById('media-{id}');if(e)e.src='{dataUri}';}})();";
        await MainThread.InvokeOnMainThreadAsync(() => webView.EvaluateJavaScriptAsync(js));
        onProgress?.Invoke(i + 1, total);
      }
      catch { }
    }
  }

  public async Task<string> GetFullResDataUriAsync(string mediaId)
  {
    try
    {
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

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^- (.+)$",
        "<ul><li>$1</li></ul>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = processedText.Replace("<ul><li>", "<li>");
    processedText = processedText.Replace("</li></ul>", "</li>");
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"<li>(.+?)</li>\s*<li>",
        "<li>$1</li><li>"
    );
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^<li>",
        "<ul><li>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"</li>$",
        "</li></ul>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^(\d+)\. (.+)$",
        "<ol><li>$2</li></ol>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = processedText.Replace("<ol><li>", "<li>");
    processedText = processedText.Replace("</li></ol>", "</li>");
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"<li>(.+?)</li>\s*<li>",
        "<li>$1</li><li>"
    );
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^<li>",
        "<ol><li>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );
    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"</li>$",
        "</li></ol>",
        System.Text.RegularExpressions.RegexOptions.Multiline
    );

    processedText = System.Text.RegularExpressions.Regex.Replace(
        processedText,
        @"^```([^\n`]*)\n([\s\S]*?)^```$",
        m =>
        {
            var lang = m.Groups[1].Value.Trim();
            var code = m.Groups[2].Value.TrimEnd('\n');
            var langClass = string.IsNullOrEmpty(lang) ? "" : $" class=\"language-{lang}\"";
            return $"<pre><code{langClass}>{code}</code></pre>";
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

    processedText = processedText.Replace("<p><h", "<h");
    processedText = processedText.Replace("</h1></p>", "</h1>");
    processedText = processedText.Replace("</h2></p>", "</h2>");
    processedText = processedText.Replace("</h3></p>", "</h3>");
    processedText = processedText.Replace("<p><pre>", "<pre>");
    processedText = processedText.Replace("</pre></p>", "</pre>");
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