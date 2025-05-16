namespace Notes.Services.Markdown;

public class MarkdownProcessor
{
  private readonly List<ISyntaxExtension> _extensions = new List<ISyntaxExtension>();
  private readonly Services.Notes.MediaManager _mediaManager;

  public MarkdownProcessor(Services.Notes.MediaManager mediaManager)
  {
    _mediaManager = mediaManager;
  }

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

  public async Task<string> ProcessMediaLinksAsync(string markdown)
  {
    var regex = new System.Text.RegularExpressions.Regex(@"!\[(.*?)\]\(media:(.*?)\)");
    var matches = regex.Matches(markdown);

    foreach (System.Text.RegularExpressions.Match match in matches)
    {
      string altText = match.Groups[1].Value;
      string mediaId = match.Groups[2].Value;

      var mediaItem = await _mediaManager.GetMediaAsync(mediaId);
      if (mediaItem != null)
      {
        string dataUri = await GetMediaDataUriAsync(mediaId);
        string imgTag = $"<img src=\"{dataUri}\" alt=\"{altText}\" />";
        markdown = markdown.Replace(match.Value, imgTag);
      }
      else
      {
        string imgTag = $"<img alt=\"{altText} (не найдено)\" />";
        markdown = markdown.Replace(match.Value, imgTag);
      }
    }

    return markdown;
  }

  private async Task<string> GetMediaDataUriAsync(string mediaId)
  {
    try
    {
      using (Stream stream = await _mediaManager.GetMediaContentAsync(mediaId))
      using (MemoryStream ms = new MemoryStream())
      {
        await stream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();

        var mediaItem = await _mediaManager.GetMediaAsync(mediaId);
        string fileType = mediaItem?.FileType?.ToLowerInvariant() ?? "png";
        string mimeType = "image/png";

        switch (fileType)
        {
          case "jpg":
          case "jpeg":
            mimeType = "image/jpeg";
            break;
          case "png":
            mimeType = "image/png";
            break;
          case "gif":
            mimeType = "image/gif";
            break;
          case "webp":
            mimeType = "image/webp";
            break;
        }

        string base64 = Convert.ToBase64String(bytes);
        return $"data:{mimeType};base64,{base64}";
      }
    }
    catch
    {
      return "";
    }
  }

  private string ProcessBasicMarkdown(string markdown)
  {
    var processedText = markdown;

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
        @"^```(.+?)$\n(.*?)^```$",
        m => $"<pre><code class=\"language-{m.Groups[1].Value.Trim()}\">{m.Groups[2].Value}</code></pre>",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline
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

    return processedText;
  }
}