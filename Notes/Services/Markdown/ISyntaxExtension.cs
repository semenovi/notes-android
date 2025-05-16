namespace Notes.Services.Markdown;

public interface ISyntaxExtension
{
  string Name { get; }
  string Description { get; }
  string Process(string markdown);
}