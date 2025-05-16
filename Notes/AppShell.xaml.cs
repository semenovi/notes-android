namespace Notes;

public partial class AppShell : Shell
{
  public AppShell()
  {
    InitializeComponent();

    Routing.RegisterRoute(nameof(Views.Pages.NotesPage), typeof(Views.Pages.NotesPage));
    Routing.RegisterRoute(nameof(Views.Pages.NoteEditorPage), typeof(Views.Pages.NoteEditorPage));
  }
}