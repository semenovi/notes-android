using Microsoft.Extensions.Logging;
using Notes.Data.Repositories;
using Notes.Data.Storage;
using Notes.Services.Crypto;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using Notes.Services.Sync;
using Notes.Views.Pages;

namespace Notes;

public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
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

    builder.Services.AddSingleton<SyncManager>();
    builder.Services.AddSingleton<ISyncAdapter, UsbSyncAdapter>();
    builder.Services.AddSingleton<ISyncAdapter, NetworkSyncAdapter>();

    builder.Services.AddTransient<FoldersPage>();
    builder.Services.AddTransient<NotesPage>();
    builder.Services.AddTransient<NoteEditorPage>();
    builder.Services.AddTransient<MarkdownPreviewPage>();

#if DEBUG
    builder.Logging.AddDebug();
#endif

    return builder.Build();
  }
}