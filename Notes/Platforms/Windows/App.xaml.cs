using Microsoft.UI.Xaml;

namespace Notes.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        UnhandledException += (_, e) => StartupTrace.Log("unhandled", e.Exception);
        try
        {
            this.InitializeComponent();
        }
        catch (Exception ex)
        {
            StartupTrace.Log("app-init", ex);
            throw;
        }
    }

    protected override MauiApp CreateMauiApp()
    {
        try
        {
            return MauiProgram.CreateMauiApp();
        }
        catch (Exception ex)
        {
            StartupTrace.Log("create-maui-app", ex);
            throw;
        }
    }
}

internal static class StartupTrace
{
    internal static void Log(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "notes_crash.log"),
                $"{DateTime.Now:O} [{source}] {ex}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
