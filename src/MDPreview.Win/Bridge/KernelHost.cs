using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MDPreview.Win.Bridge;

/// <summary>
/// WebView2 宿主：内核产物与本地图片都从 <c>https://mdpreview.local</c> 同源提供，
/// 由壳在 <see cref="CoreWebView2.WebResourceRequested"/> 里读盘应答（页面带 CSP 头）。
/// 不启本地 HTTP 服务、不注册自定义 scheme、不做目录映射——只有一条受控的读入口。
/// </summary>
public sealed class KernelHost
{
    const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self' data:; media-src 'self' data:; " +
        "connect-src 'none'; object-src 'none'; base-uri 'none'; frame-src 'none'; form-action 'none'";

    readonly WebView2 _web;

    public KernelHost(WebView2 web)
    {
        _web = web;
    }

    public KernelClient? Client { get; private set; }
    public CoreWebView2 Core { get; private set; } = null!;

    public event Action<string, JsonObject>? Notification;
    public event Action<string>? Shortcut;

    /// <summary>页面里拖入的图片（mime + base64），壳落盘后插入。</summary>
    public event Action<string, string>? DroppedImage;

    public Func<string, JsonObject, Task<JsonNode?>>? RequestHandler { get; set; }

    public bool DevTools { get; set; }

    public async Task InitializeAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(AppInfo.UserDataDirectory, "WebView2"),
            new CoreWebView2EnvironmentOptions("--disable-features=msWebOOUI,msPdfOOUI"));

        await _web.EnsureCoreWebView2Async(env);
        Core = _web.CoreWebView2;

        var settings = Core.Settings;
        // 编辑型应用需要系统的剪切/复制/粘贴菜单；浏览器专属项由 WebView2 按上下文裁剪
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreDevToolsEnabled = DevTools;
        // 必须关：Ctrl+S/O/P/F/±/0 是 Chromium 的浏览器级加速键，开着时不会派发 DOM keydown，
        // 壳级快捷键（前导脚本）就收不到。编辑键（Ctrl+C/V/X/Z/A）不在此开关范围，保持原生。
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsSwipeNavigationEnabled = false;

        Client = new KernelClient(Core);
        Client.Notification += (op, payload) => Notification?.Invoke(op, payload);
        Client.Shortcut += key => Shortcut?.Invoke(key);
        Client.DroppedImage += (mime, data) => DroppedImage?.Invoke(mime, data);
        Client.RequestHandler = (op, payload) => RequestHandler?.Invoke(op, payload) ?? Task.FromResult<JsonNode?>(null);

        Core.AddWebResourceRequestedFilter("https://mdpreview.local/*", CoreWebView2WebResourceContext.All);
        Core.WebResourceRequested += OnWebResourceRequested;

        Core.NewWindowRequested += (_, e) => e.Handled = true;
        Core.DownloadStarting += (_, e) => e.Cancel = true;
        Core.PermissionRequested += (_, e) => e.Handled = true;

        await Core.AddScriptToExecuteOnDocumentCreatedAsync(LoadPrelude());
        Core.Navigate(AppInfo.PageUrl);
    }

    static string LoadPrelude()
    {
        var assembly = typeof(KernelHost).Assembly;
        using var stream = assembly.GetManifestResourceStream("webview2-prelude.js")
            ?? throw new InvalidOperationException("webview2-prelude.js missing from resources");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        using var deferral = e.GetDeferral();
        try
        {
            var uri = e.Request.Uri;
            if (uri.StartsWith(AppInfo.ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                ServeLocal(ResourceUri.ToPath(uri), e);
                return;
            }

            var requestPath = new Uri(uri).AbsolutePath;
            if (requestPath is "" or "/") requestPath = "/index.html";
            var full = Path.GetFullPath(Path.Combine(
                AppInfo.WebDirectory, requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

            if (!full.StartsWith(AppInfo.WebDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                e.Response = Core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            ServeFile(full, e, withCsp: string.Equals(requestPath, "/index.html", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Warn($"web resource failed: {ex.Message}");
            e.Response = Core.Environment.CreateWebResourceResponse(null, 500, "Internal Error", "");
        }
        finally
        {
            deferral.Complete();
        }
    }

    void ServeLocal(string? path, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (path is null || !File.Exists(path))
        {
            e.Response = Core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        ServeFile(path, e, withCsp: false);
    }

    void ServeFile(string path, CoreWebView2WebResourceRequestedEventArgs e, bool withCsp)
    {
        var bytes = File.ReadAllBytes(path);
        var headers = "Content-Type: " + MimeTypes.For(path) + "\r\nCache-Control: no-cache";
        if (withCsp) headers += "\r\nContent-Security-Policy: " + Csp;
        e.Response = Core.Environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", headers);
    }
}

/// <summary>
/// <c>https://mdpreview.local/__res/C:/a b/x.png</c> → <c>C:\a b\x.png</c>
/// （与内核 PathPolicy 的编码口径对齐）。
/// </summary>
public static class ResourceUri
{
    public static string? ToPath(string uri)
    {
        var prefix = AppInfo.ResourcePrefix;
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = uri[prefix.Length..];
        var decoded = string.Join('/', rest.Split('/').Select(segment =>
        {
            try { return Uri.UnescapeDataString(segment); }
            catch { return segment; }
        }));

        if (decoded.StartsWith("//", StringComparison.Ordinal))
            return "\\\\" + decoded[2..].Replace('/', '\\');
        if (decoded.Length > 2 && decoded[0] == '/' && char.IsLetter(decoded[1]) && decoded[2] == ':')
            decoded = decoded[1..];
        return decoded.Replace('/', '\\');
    }
}

internal static class MimeTypes
{
    public static string For(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".map" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".eot" => "application/vnd.ms-fontobject",
        _ => "application/octet-stream",
    };
}
