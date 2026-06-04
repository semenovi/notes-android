using Microsoft.Extensions.Logging;
using Notes.Data.Repositories;
using Notes.Data.Storage;
using Notes.Services.Crypto;
using Notes.Services.Export;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using Notes.Services.Sync;
using Notes.Services;
using Notes.Views.Pages;
using Notes.Views.Windows;
using Notes.Views.Windows.Controls;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;

[assembly: Microsoft.Maui.Controls.ExportFont("MaterialIcons-Regular.ttf", Alias = "MaterialIcons")]
[assembly: Microsoft.Maui.Controls.ExportFont("OpenSans-Regular.ttf", Alias = "OpenSansRegular")]
[assembly: Microsoft.Maui.Controls.ExportFont("OpenSans-Semibold.ttf", Alias = "OpenSansSemibold")]

namespace Notes;

public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseMauiCommunityToolkit()
        .ConfigureFonts(fonts =>
        {
          fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
          fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
          fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
        });

    string appDataPath = Path.Combine(FileSystem.AppDataDirectory, "Notes");

    builder.Services.AddSingleton<FileSystemStorage>(new FileSystemStorage(appDataPath));
    builder.Services.AddSingleton<MediaStorage>();
    builder.Services.AddSingleton<NoteRepository>();
    builder.Services.AddSingleton<FolderRepository>();

    builder.Services.AddSingleton<NoteManager>();
    builder.Services.AddSingleton<FolderManager>();
    builder.Services.AddSingleton<MediaManager>();

    builder.Services.AddSingleton<MarkdownProcessor>();
    builder.Services.AddSingleton<SyntaxExtensionManager>();

    builder.Services.AddSingleton<CryptoService>();
    builder.Services.AddSingleton<Services.Crypto.SecureStorage>();

    builder.Services.AddSingleton<DebugLogService>();
    builder.Services.AddSingleton<ToastService>();
    builder.Services.AddSingleton<ProgressNotificationService>();
    builder.Services.AddSingleton<SyncSettingsService>();
    builder.Services.AddSingleton<ISyncAdapter, UsbSyncAdapter>();
    builder.Services.AddSingleton<ISyncAdapter, NetworkSyncAdapter>();
    builder.Services.AddSingleton<SyncManager>();
    builder.Services.AddSingleton<ReactiveSyncService>();

    builder.Services.AddSingleton<IFileSaver>(FileSaver.Default);
    builder.Services.AddSingleton<ExportService>();

#if WINDOWS
        builder.Services.AddTransient<MainWindow>();
        builder.Services.AddTransient<WindowsFolderTreeView>();
        builder.Services.AddTransient<WindowsNoteListView>();
        builder.Services.AddTransient<WindowsNoteEditor>();
#else
    builder.Services.AddTransient<FoldersPage>();
    builder.Services.AddTransient<NotesPage>();
    builder.Services.AddTransient<NoteEditorPage>();
    builder.Services.AddTransient<NoteViewPage>();
#endif

    builder.Services.AddTransient<MarkdownPreviewPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

    return builder.Build();
  }
}