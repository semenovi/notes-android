using System.Linq;

namespace Notes.Helpers;

// shares scroll position between the markdown preview and the plain-text editor via a
// common currency: the source markdown line number. Each renderer maps that line to its
// own scroll target using its own layout - the WebView by finding the actual tagged
// block element (data-src-line, see MarkdownProcessor), the editor by line-count
// proportion of its own extent. A raw pixel-fraction of "scrollable range" doesn't work
// because the two renderers lay the same content out at very different heights (markdown
// blocks/images/headings vs. uniform monospace lines).
public static class EditorScrollSync
{
  // uses the browser's own hit-testing (what's actually painted a few pixels below the
  // top edge) rather than comparing block top-edge coordinates against a fixed
  // threshold: with the latter, when the scroll position lands inside a block's CSS
  // margin (the gap between two tagged blocks), the block that's almost entirely
  // scrolled past kept winning over the one that's actually filling the viewport
  private const string GetVisibleSourceLineJs =
      "(function(){" +
      "var probe=document.elementFromPoint(window.innerWidth/2,10);" +
      "while(probe&&!probe.hasAttribute('data-src-line'))probe=probe.parentElement;" +
      "if(probe)return parseInt(probe.getAttribute('data-src-line'));" +
      "var els=document.querySelectorAll('[data-src-line]');" +
      "for(var i=0;i<els.length;i++){" +
      "if(els[i].getBoundingClientRect().bottom>0)return parseInt(els[i].getAttribute('data-src-line'));" +
      "}" +
      "return 0;" +
      "})();";

  public static async Task<int> GetVisibleSourceLineAsync(WebView webView)
  {
    try
    {
      string result = await webView.EvaluateJavaScriptAsync(GetVisibleSourceLineJs);
      if (int.TryParse(result, out int line))
        return line;
    }
    catch { }
    return 0;
  }

  public static async Task ScrollToSourceLineAsync(WebView webView, int line)
  {
    try
    {
      // not every line has a tagged block (blank lines, lines mid-way through a fenced
      // code block) - fall back to the closest tagged line at or before the target
      string js =
          "(function(){" +
          $"var target={line};" +
          "var els=document.querySelectorAll('[data-src-line]');" +
          "var best=els.length>0?els[0]:null;" +
          "for(var i=0;i<els.length;i++){" +
          "var l=parseInt(els[i].getAttribute('data-src-line'));" +
          "if(l<=target)best=els[i];else break;" +
          "}" +
          "if(best)window.scrollTo(0,best.offsetTop);" +
          "})();";
      await webView.EvaluateJavaScriptAsync(js);
    }
    catch { }
  }

  // normalizes line endings first: WinUI's native TextBox hands back multi-line text
  // with bare \r rather than \n, so counting raw '\n' occurrences undercounts lines to
  // just 1 for anything read via ContentEditor.Text on Windows
  public static int GetLineCount(string content)
  {
    if (string.IsNullOrEmpty(content))
      return 1;
    return content.Replace("\r\n", "\n").Replace("\r", "\n").Count(c => c == '\n') + 1;
  }

  public static int GetCharOffsetForLine(string content, int line)
  {
    if (string.IsNullOrEmpty(content) || line <= 0)
      return 0;

    var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    int count = Math.Min(line, lines.Length);
    int offset = 0;
    for (int i = 0; i < count; i++)
      offset += lines[i].Length + 1;

    return Math.Min(offset, content.Length);
  }

  // carries the editor's source line back to the preview when the user leaves the
  // editor for the same note. Keyed by note id and consumed once (TryTake) so it
  // doesn't stick around for unrelated re-appearances of the preview page
  private static readonly Dictionary<string, int> _pendingReturnLine = new();

  public static void SetReturnLine(string noteId, int line)
  {
    if (string.IsNullOrEmpty(noteId))
      return;
    _pendingReturnLine[noteId] = Math.Max(0, line);
  }

  public static bool TryTakeReturnLine(string noteId, out int line)
  {
    line = 0;
    if (string.IsNullOrEmpty(noteId) || !_pendingReturnLine.TryGetValue(noteId, out line))
      return false;

    _pendingReturnLine.Remove(noteId);
    return true;
  }
}
