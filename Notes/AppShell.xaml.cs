using Notes.Services;

namespace Notes;

public partial class AppShell : Shell
{
  public AppShell()
  {
#if !WINDOWS
    InitializeComponent();

    Routing.RegisterRoute(nameof(Views.Pages.NotesPage), typeof(Views.Pages.NotesPage));
    Routing.RegisterRoute(nameof(Views.Pages.NoteEditorPage), typeof(Views.Pages.NoteEditorPage));
    Routing.RegisterRoute(nameof(Views.Pages.NoteViewPage), typeof(Views.Pages.NoteViewPage));

    var toastService = IPlatformApplication.Current.Services.GetRequiredService<ToastService>();
    toastService.ToastRequested += OnToastRequested;
#endif
  }

#if !WINDOWS
  private void OnToastRequested(string message)
    => _ = ShowSnackbarAsync(message);

  private static async Task ShowSnackbarAsync(string message)
  {
    using var snackbar = CommunityToolkit.Maui.Alerts.Snackbar.Make(
        message,
        duration: TimeSpan.FromSeconds(2),
        visualOptions: new CommunityToolkit.Maui.Core.SnackbarOptions
        {
            BackgroundColor = Color.FromArgb("#CC1C1C1E"),
            TextColor = Colors.White,
            CornerRadius = new CornerRadius(8),
        });
    await snackbar.Show();
  }
#endif
}
