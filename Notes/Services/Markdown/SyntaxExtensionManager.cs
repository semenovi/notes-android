namespace Notes.Services.Markdown;

public class SyntaxExtensionManager
{
  private readonly List<ISyntaxExtension> _extensions = new List<ISyntaxExtension>();

  public void RegisterExtension(ISyntaxExtension extension)
  {
    if (!_extensions.Any(e => e.Name == extension.Name))
    {
      _extensions.Add(extension);
    }
  }

  public ISyntaxExtension? GetExtension(string name)
  {
    return _extensions.FirstOrDefault(e => e.Name == name);
  }

  public List<ISyntaxExtension> GetAllExtensions()
  {
    return _extensions.ToList();
  }
}