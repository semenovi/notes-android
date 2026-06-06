using Microsoft.UI;
using Microsoft.UI.Windowing;
using Notes.Services.Notes;
using WinRT.Interop;

namespace Notes.Views.Windows;

public class ImageViewerPage : ContentPage
{
    private readonly WebView _webView;
    private readonly MediaManager? _mediaManager;
    private readonly string? _mediaId;

    public ImageViewerPage(string imageSrc, MediaManager? mediaManager = null, string? mediaId = null)
    {
        _mediaManager = mediaManager;
        _mediaId = mediaId;
        BackgroundColor = Microsoft.Maui.Graphics.Colors.Black;
        _webView = new WebView();
        _webView.Navigating += OnNavigating;
        Content = _webView;

        // Load asynchronously: data URIs can be several MB, write to a temp file
        // so WebView2 reads it from disk (avoids NavigateToString 2 MB limit and
        // cross-origin restrictions that block file:// images in HtmlWebViewSource).
        _ = LoadAsync(imageSrc);
    }

    private async Task LoadAsync(string imageSrc)
    {
        var html     = BuildHtml(imageSrc, _mediaId != null);
        var htmlPath = Path.Combine(FileSystem.CacheDirectory, "iv_page.html");
        await File.WriteAllTextAsync(htmlPath, html, System.Text.Encoding.UTF8);
        var fileUrl  = new Uri(htmlPath).AbsoluteUri;
        await MainThread.InvokeOnMainThreadAsync(
            () => _webView.Source = new UrlWebViewSource { Url = fileUrl });
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("img-viewer://close"))
        {
            e.Cancel = true;
            Application.Current!.CloseWindow(this.Window!);
        }
        else if (e.Url.StartsWith("img-download://") && _mediaManager != null && _mediaId != null)
        {
            e.Cancel = true;
            _ = DownloadMediaAsync();
        }
    }

    private async Task DownloadMediaAsync()
    {
        if (_mediaManager == null || _mediaId == null) return;
        try
        {
            var item = await _mediaManager.GetMediaAsync(_mediaId);
            if (item == null) return;
            byte[] bytes = await _mediaManager.GetRawContentAsync(_mediaId);
            string downloadsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string baseName = string.IsNullOrEmpty(item.FileName)
                ? $"{_mediaId}.{item.FileType}"
                : item.FileName;
            string filePath = Path.Combine(downloadsDir, baseName);
            if (File.Exists(filePath))
            {
                string ext = Path.GetExtension(baseName);
                string name = Path.GetFileNameWithoutExtension(baseName);
                int i = 1;
                while (File.Exists(filePath))
                    filePath = Path.Combine(downloadsDir, $"{name}({i++}){ext}");
            }
            await File.WriteAllBytesAsync(filePath, bytes);
        }
        catch { }
    }

    internal static void ConfigureWindow(Microsoft.Maui.Controls.Window mauiWindow)
    {
        var appWindow = GetAppWindow(mauiWindow);
        if (appWindow is null) return;
        appWindow.IsShownInSwitchers = false;
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    internal static AppWindow? GetAppWindow(Microsoft.Maui.Controls.Window? mauiWindow)
    {
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win) return null;
        var hwnd = WindowNative.GetWindowHandle(win);
        return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
    }

    private static string BuildHtml(string imageSrc, bool hasDownload)
    {
        // imageSrc is either a data URI (data:image/jpeg;base64,...) or an https:// URL.
        // It is embedded directly into the HTML so WebView2 never needs to fetch a
        // separate file:// resource — no cross-origin issues regardless of page origin.
        string downloadBtn = hasDownload
            ? "#dl{position:fixed;top:16px;right:16px;background:rgba(255,255,255,0.15);color:#fff;" +
              "border:none;border-radius:8px;padding:8px 14px;font-size:14px;cursor:pointer;" +
              "backdrop-filter:blur(6px);transition:background 0.15s;z-index:10;}" +
              "#dl:hover{background:rgba(255,255,255,0.28);}"
            : "";
        string downloadHtml = hasDownload
            ? "<button id=\"dl\" title=\"Save to Downloads\">&#8681; Save</button>"
            : "";
        string downloadScript = hasDownload
            ? "document.getElementById('dl').addEventListener('click',function(e){" +
              "e.stopPropagation();" +
              "window.location.href='img-download://save';" +
              "});"
            : "";

        string template =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" +
            "*{margin:0;padding:0;box-sizing:border-box;}" +
            "html,body{width:100%;height:100%;background:#000;overflow:hidden;}" +
            "#c{display:flex;align-items:center;justify-content:center;width:100%;height:100%;}" +
            "#i{max-width:95vw;max-height:95vh;object-fit:contain;cursor:zoom-in;" +
               "transform-origin:center;user-select:none;}" +
            "body{animation:fi 0.12s ease;}@keyframes fi{from{opacity:0}to{opacity:1}}" +
            downloadBtn +
            "</style></head><body>" +
            downloadHtml +
            "<div id=\"c\"><img id=\"i\" draggable=\"false\"/></div>" +
            "<script>(function(){" +
            "var img=document.getElementById('i');" +
            "var cnt=document.getElementById('c');" +
            "var sc=1,tx=0,ty=0;" +
            "var dragging=false,moved=false,mx=0,my=0;" +
            "img.src=IMGSRC;" +
            "function xf(){img.style.transform='translate('+tx+'px,'+ty+'px) scale('+sc+')';}" +
            "cnt.addEventListener('wheel',function(e){" +
              "e.preventDefault();" +
              "var f=e.deltaY<0?1.12:1/1.12;" +
              "sc=Math.min(Math.max(sc*f,0.2),8);" +
              "img.style.cursor=sc>1?'grab':'zoom-in';" +
              "xf();" +
            "},{passive:false});" +
            "img.addEventListener('mousedown',function(e){" +
              "moved=false;mx=e.clientX;my=e.clientY;" +
              "if(sc>1){dragging=true;img.style.cursor='grabbing';e.preventDefault();}" +
            "});" +
            "window.addEventListener('mousemove',function(e){" +
              "if(!dragging)return;" +
              "if(Math.abs(e.clientX-mx)>3||Math.abs(e.clientY-my)>3)moved=true;" +
              "tx+=e.clientX-mx;ty+=e.clientY-my;mx=e.clientX;my=e.clientY;xf();" +
            "});" +
            "window.addEventListener('mouseup',function(){" +
              "dragging=false;" +
              "img.style.cursor=sc>1?'grab':'zoom-in';" +
            "});" +
            "cnt.addEventListener('click',function(){" +
              "if(!moved)window.location.href='img-viewer://close';" +
              "moved=false;" +
            "});" +
            "document.addEventListener('keydown',function(e){" +
              "if(e.key==='Escape')window.location.href='img-viewer://close';" +
            "});" +
            downloadScript +
            "})();</script></body></html>";

        // Single quotes never appear in data URIs (base64 alphabet) or standard https:// URLs.
        return template.Replace("IMGSRC", $"'{imageSrc.Replace("'", "\\'")}'");
    }
}
