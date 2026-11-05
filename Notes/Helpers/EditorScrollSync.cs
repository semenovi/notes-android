using System.Linq;

namespace Notes.Helpers;

public static class EditorScrollSync
{

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
