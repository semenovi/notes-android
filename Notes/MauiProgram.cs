using Microsoft.Extensions.Logging;
using Notes.Services.Markdown;
using Notes.Services.Notes;
using Notes.Services.Security;
using Notes.Services.Storage;
using Notes.Services.Sync;

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
            });

        // Register services
        builder.Services.AddSingleton<FileSystemStorage>(provider => 
            new FileSystemStorage("Notes"));
            
        builder.Services.AddSingleton<MediaStorage>();
        builder.Services.AddSingleton<NoteRepository>();
        builder.Services.AddSingleton<FolderManager>();
        
        builder.Services.AddSingleton<CryptoService>();
        builder.Services.AddSingleton<SecureStorageImpl>();
        
        builder.Services.AddSingleton<MarkdownProcessor>();
        builder.Services.AddSingleton<SyntaxExtensionManager>();
        
        builder.Services.AddSingleton<SyncManager>();
        builder.Services.AddSingleton<UsbSyncAdapter>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}