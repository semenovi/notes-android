using System.Text.RegularExpressions;

namespace Notes.Services.Markdown;

public static class TaskListMarkdown
{
  public static readonly Regex TaskLineRegex = new(
      @"^[ \t]*- \[( |x|X)\](?: (.*))?$",
      RegexOptions.Multiline | RegexOptions.Compiled);

  public const string UrlScheme = "task-toggle://";

  public const string Css =
      ".task-item{display:flex;align-items:flex-start;gap:8px;margin:0 0 6px;}" +
      ".task-checkbox{width:18px;height:18px;margin:2px 0 0;flex:none;accent-color:#007AFF;}" +
      ".task-text.done{text-decoration:line-through;color:#8E8E93;}";

  public const string Script = """
      <script>
      document.addEventListener('change', function(e) {
        var cb = e.target;
        if (!cb.classList || !cb.classList.contains('task-checkbox')) return;
        var text = cb.nextElementSibling;
        if (text) text.classList.toggle('done', cb.checked);
        window.location.href = 'task-toggle://' + cb.getAttribute('data-task-index')
            + '/' + (cb.checked ? '1' : '0');
      });
      </script>
      """;

  public static string? Toggle(string content, int index, bool isChecked)
  {
    var matches = TaskLineRegex.Matches(content);
    if (index < 0 || index >= matches.Count)
      return null;

    var stateGroup = matches[index].Groups[1];
    return content.Remove(stateGroup.Index, 1)
                  .Insert(stateGroup.Index, isChecked ? "x" : " ");
  }

  public static bool TryParseToggleUrl(string url, out int index, out bool isChecked)
  {
    index = -1;
    isChecked = false;
    if (!url.StartsWith(UrlScheme)) return false;
    var parts = url[UrlScheme.Length..].Split('/');
    if (parts.Length != 2 || !int.TryParse(parts[0], out index)) return false;
    isChecked = parts[1] == "1";
    return true;
  }
}
