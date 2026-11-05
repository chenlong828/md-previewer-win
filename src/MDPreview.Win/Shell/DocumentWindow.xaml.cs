using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MDPreview.Win.Bridge;
using MDPreview.Win.Io;
using MDPreview.Win.Settings;
using Microsoft.Win32;

namespace MDPreview.Win.Shell;

/// <summary>一文档一窗口：菜单、状态栏、文档生命周期与内核请求应答。</summary>
public partial class DocumentWindow : Window
{
    const string Product = "MDPreview";

    readonly AppSettings _settings;
    KernelHost? _host;
    KernelClient? _kernel;

    string? _path;
    string? _deferredPath;
    readonly string? _initialPath;
    TextFile.TextDocument? _doc;
    FileSystemWatcher? _watcher;
    DateTime _lastWriteUtc;

    bool _dirty;
    bool _ready;
    bool _closingConfirmed;

    readonly MenuItem _recentMenu = new() { Header = "最近打开" };
    readonly MenuItem _sourceMenu = new() { Header = "源码模式", IsCheckable = true };
    readonly MenuItem _minimapMenu = new() { Header = "显示文档缩略图", IsCheckable = true };

    public DocumentWindow(AppSettings settings, string? path = null)
    {
        _settings = settings;
        _initialPath = path;
        InitializeComponent();
        BuildMenu();
        _minimapMenu.IsChecked = settings.MinimapVisible;

        Loaded += async (_, _) => await InitializeAsync();
        SourceInitialized += (_, _) => ApplyChrome(IsDark);
        Closing += OnClosing;
        Activated += (_, _) => UpdateRecentMenu();
    }

    public string? DocumentPath => _path;
    public bool IsDirty => _dirty;

    // ---------------------------------------------------------------- 启动

    async Task InitializeAsync()
    {
        if (_host is not null) return;

        if (!AppInfo.HasKernel)
        {
            OverlayText.Text = "找不到内核产物（web/index.html）。\n请先运行 build.ps1 构建内核。";
            Log.Error("kernel assets missing at " + AppInfo.WebDirectory);
            return;
        }

        try
        {
            _host = new KernelHost(Web);
            _host.RequestHandler = HandleKernelRequest;
            _host.Notification += OnKernelNotification;
            _host.Shortcut += OnShortcut;
            await _host.InitializeAsync();
            _kernel = _host.Client;

            var pending = _initialPath ?? _deferredPath;
            if (pending is not null && File.Exists(pending)) await OpenInitialAsync(pending);
            else await SendInitAsync("", null);
        }
        catch (Exception ex)
        {
            OverlayText.Text = "编辑器初始化失败：" + ex.Message;
            Log.Error("webview init failed", ex);
        }
    }

    /// <summary>首篇文档必须随 init 一起送入：init 是 ready 之前唯一允许的 op。</summary>
    async Task OpenInitialAsync(string path)
    {
        var doc = TextFile.Read(path);
        _doc = doc;
        await SendInitAsync(doc.Text, path);
        _lastWriteUtc = File.GetLastWriteTimeUtc(path);
        _settings.AddRecent(path);
        UpdateTitle();
        UpdateRecentMenu();
        StartWatcher(path);
        Log.Info("opened " + path);
    }

    async Task SendInitAsync(string markdown, string? path)
    {
        _path = path;
        var baseDir = path is null ? "" : Path.GetDirectoryName(path)!;
        var opts = new JsonObject
        {
            ["protocolVersion"] = AppInfo.ProtocolVersion,
            ["baseDir"] = baseDir,
            ["theme"] = IsDark ? "dark" : "light",
            ["zoom"] = _settings.Zoom,
            ["minimapVisible"] = _settings.MinimapVisible,
            ["title"] = Path.GetFileName(path) ?? "",
            ["plugins"] = "full",
            ["pathPolicy"] = new JsonObject { ["urlPrefix"] = AppInfo.ResourcePrefix, ["style"] = "win32" },
        };
        var payload = new JsonObject { ["markdown"] = markdown, ["opts"] = opts };
        await _kernel!.SendAsync("init", payload, TimeSpan.FromSeconds(30));

        var handshake = _kernel.Handshake;
        var protocol = handshake?["protocolVersion"]?.GetValue<int>() ?? 0;
        var version = handshake?["kernelVersion"]?.GetValue<string>() ?? "unknown";
        var ops = (handshake?["ops"] as JsonArray)?.Count ?? 0;
        if (protocol != AppInfo.ProtocolVersion)
            throw new InvalidOperationException($"内核协议版本不匹配：页面 {protocol}，壳 {AppInfo.ProtocolVersion}");
        Log.Info($"handshake kernel={version} protocol={protocol} ops={ops}");
    }

    // ---------------------------------------------------------------- 打开 / 保存

    public async Task OpenPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "文件不存在：" + path, Product, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_kernel is null) { _deferredPath = path; return; }

        try
        {
            var doc = TextFile.Read(path);
            _doc = doc;
            _path = path;
            _lastWriteUtc = File.GetLastWriteTimeUtc(path);
            await _kernel.SendAsync("setBaseDir", new JsonObject { ["path"] = Path.GetDirectoryName(path)! });
            await _kernel.SendAsync("setMarkdown", new JsonObject
            {
                ["md"] = doc.Text,
                ["baseDir"] = Path.GetDirectoryName(path)!,
            }, TimeSpan.FromSeconds(20));
            await _kernel.SendAsync("setTitle", new JsonObject { ["title"] = Path.GetFileName(path) });
            _dirty = false;
            _settings.AddRecent(path);
            UpdateTitle();
            UpdateRecentMenu();
            StartWatcher(path);
            Log.Info("opened " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开失败：" + ex.Message, Product, MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error("open failed", ex);
        }
    }

    async Task NewDocumentAsync()
    {
        if (_kernel is null) return;
        await _kernel.SendAsync("setMarkdown", new JsonObject { ["md"] = "", ["baseDir"] = "" });
        _path = null;
        _doc = null;
        _dirty = false;
        UpdateTitle();
    }

    async Task SaveAsync(bool saveAs)
    {
        if (_kernel is null) return;

        var target = _path;
        if (target is null || saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|所有文件 (*.*)|*.*",
                FileName = target is null ? "未命名.md" : Path.GetFileName(target),
                InitialDirectory = target is null ? _settings.LastFolder : Path.GetDirectoryName(target),
                DefaultExt = ".md",
            };
            if (dialog.ShowDialog(this) != true) return;
            target = dialog.FileName;
        }

        try
        {
            var answer = await _kernel.SendAsync("getSaveMarkdown");
            var markdown = answer?["md"]?.GetValue<string>() ?? "";

            if (_doc is null) _doc = new TextFile.TextDocument("", new System.Text.UTF8Encoding(false), false, "\n");
            TextFile.WriteAtomic(target, markdown, _doc.Encoding, _doc.HasBom, _doc.NewLine);

            _path = target;
            _lastWriteUtc = File.GetLastWriteTimeUtc(target);
            _settings.LastFolder = Path.GetDirectoryName(target);
            _settings.AddRecent(target);
            await _kernel.SendAsync("setBaseDir", new JsonObject { ["path"] = Path.GetDirectoryName(target)! });
            await _kernel.SendAsync("setTitle", new JsonObject { ["title"] = Path.GetFileName(target) });
            await _kernel.SendAsync("markClean");
            _dirty = false;
            UpdateTitle();
            UpdateRecentMenu();
            StartWatcher(target);
            Log.Info("saved " + target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, Product, MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error("save failed", ex);
        }
    }

    // ---------------------------------------------------------------- 内核通知与请求

    void OnKernelNotification(string op, JsonObject payload)
    {
        switch (op)
        {
            case "ready":
                _ready = true;
                Overlay.Visibility = Visibility.Collapsed;
                UpdateState();
                Log.Info($"kernel ready protocol={payload["protocolVersion"]}");
                break;
            case "changed":
                _dirty = payload["dirty"]?.GetValue<bool>() ?? false;
                UpdateTitle();
                break;
            case "stats":
                StatsText.Text = $"字数 {payload["words"]} · 字符 {payload["chars"]}";
                break;
            case "sourceMode":
                _sourceMenu.IsChecked = payload["value"]?.GetValue<bool>() ?? false;
                break;
            case "jserror":
                Log.Warn("kernel js error: " + payload["message"]);
                break;
        }
    }

    Task<JsonNode?> HandleKernelRequest(string op, JsonObject payload) => op switch
    {
        "resolveResources" => Task.FromResult<JsonNode?>(ResolveResources(payload)),
        _ => throw new KernelException("unknown_op", $"壳未实现请求 {op}"),
    };

    JsonObject ResolveResources(JsonObject payload)
    {
        var result = new JsonObject();
        var resources = new JsonObject();
        result["resources"] = resources;

        if (payload["refs"] is not JsonArray refs) return result;
        var baseDir = payload["baseDir"]?.GetValue<string>() ?? CurrentBaseDir;

        foreach (var item in refs)
        {
            var reference = item?.GetValue<string>();
            if (string.IsNullOrEmpty(reference)) continue;

            var literal = CombinePath(baseDir, reference);
            var decoded = CombinePath(baseDir, PercentDecodeRef(reference));
            var literalExists = literal.Length > 0 && File.Exists(literal);

            var paths = new JsonArray();
            if (literalExists) paths.Add(literal);
            if (decoded.Length > 0 && !string.Equals(decoded, literal, StringComparison.OrdinalIgnoreCase) && File.Exists(decoded))
                paths.Add(decoded);

            resources[reference] = new JsonObject
            {
                ["url"] = literalExists ? ResourceUrl(literal) : null,
                ["exists"] = literalExists,
                ["isImage"] = literalExists && IsImage(literal),
                ["paths"] = paths,
            };
        }
        return result;
    }

    string CurrentBaseDir => _path is null ? "" : Path.GetDirectoryName(_path)!;

    static string CombinePath(string baseDir, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return "";
        var normalized = reference.Replace('/', '\\');
        string? combined = Path.IsPathRooted(normalized)
            ? normalized
            : (string.IsNullOrEmpty(baseDir) ? null : Path.Combine(baseDir, normalized));
        if (combined is null) return "";
        try { return Path.GetFullPath(combined); }
        catch { return ""; }
    }

    static string PercentDecodeRef(string reference) =>
        string.Join('/', reference.Split('/').Select(segment =>
        {
            try { return Uri.UnescapeDataString(segment); }
            catch { return segment; }
        }));

    static string ResourceUrl(string path)
    {
        var posix = path.Replace('\\', '/');
        var encoded = string.Join('/', posix.Split('/').Select(Uri.EscapeDataString));
        return $"{AppInfo.ResourcePrefix}/{encoded}";
    }

    static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".avif";

    // ---------------------------------------------------------------- 快捷键

    void OnShortcut(string key)
    {
        _ = key switch
        {
            "new" => NewDocumentAsync(),
            "open" => OpenViaDialogAsync(),
            "save" => SaveAsync(false),
            "saveAs" => SaveAsync(true),
            "close" => CloseWindowAsync(),
            "find" => RunOpAsync("openFindBar"),
            "toggleSource" => RunOpAsync("toggleSource"),
            "toggleDrawer" => Task.CompletedTask,
            "zoomIn" => ZoomByAsync(0.1),
            "zoomOut" => ZoomByAsync(-0.1),
            "zoomReset" => SetZoomAsync(1.0),
            _ => Task.CompletedTask,
        };
    }

    async Task RunOpAsync(string op, JsonObject? payload = null)
    {
        if (_kernel is null) return;
        await _kernel.SendAsync(op, payload);
    }

    async Task OpenViaDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|代码文件 (*.py;*.js;*.ts;*.swift;*.cs;*.json)|*.py;*.js;*.ts;*.swift;*.cs;*.json|所有文件 (*.*)|*.*",
            InitialDirectory = _settings.LastFolder,
        };
        if (dialog.ShowDialog(this) != true) return;
        _settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
        _settings.Save();
        await OpenPathAsync(dialog.FileName);
    }

    // ---------------------------------------------------------------- 视图

    bool IsDark => _settings.Theme switch
    {
        "dark" => true,
        "light" => false,
        _ => SystemPrefersDark(),
    };

    static bool SystemPrefersDark()
    {
        var value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);
        return value is int light && light == 0;
    }

    async Task SetThemeAsync(string theme)
    {
        _settings.Theme = theme;
        _settings.Save();
        var dark = IsDark;
        ApplyChrome(dark);
        if (_kernel is not null)
            await _kernel.SendAsync("setTheme", new JsonObject { ["theme"] = dark ? "dark" : "light" });
        UpdateMenuChecks();
    }

    /// <summary>壳侧配色与标题栏一起切；正文配色由内核 setTheme 负责。</summary>
    void ApplyChrome(bool dark)
    {
        Resources["ChromeBg"] = Brush(dark ? 0x202020 : 0xF6F6F6);
        Resources["ChromeFg"] = Brush(dark ? 0xEAEAEA : 0x1A1A1A);
        Resources["ChromeBorder"] = Brush(dark ? 0x3A3A3A : 0xE3E3E3);
        Resources["PageBg"] = Brush(dark ? 0x1E1E1E : 0xFFFFFF);
        Resources["MutedFg"] = Brush(dark ? 0xB0B0B0 : 0x666666);
        NativeMethods.ApplyWin11Look(this, dark);
    }

    static SolidColorBrush Brush(int rgb) =>
        new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    async Task ZoomByAsync(double delta) => await SetZoomAsync(_settings.Zoom + delta);

    async Task SetZoomAsync(double scale)
    {
        _settings.Zoom = _settings.ClampZoom(scale);
        _settings.Save();
        if (_kernel is not null)
            await _kernel.SendAsync("setZoom", new JsonObject { ["scale"] = _settings.Zoom });
    }

    async Task ToggleMinimapAsync()
    {
        _settings.MinimapVisible = !_settings.MinimapVisible;
        _settings.Save();
        _minimapMenu.IsChecked = _settings.MinimapVisible;
        if (_kernel is not null)
            await _kernel.SendAsync("setMinimapVisible", new JsonObject { ["visible"] = _settings.MinimapVisible });
    }

    // ---------------------------------------------------------------- 外部修改

    void StartWatcher(string path)
    {
        _watcher?.Dispose();
        var directory = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileName(path);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(() => OnExternalChange(path));
        _watcher.Deleted += (_, _) => Dispatcher.BeginInvoke(() => OnExternalChange(path));
        _watcher.EnableRaisingEvents = true;
    }

    void OnExternalChange(string path)
    {
        if (!File.Exists(path)) return;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (stamp <= _lastWriteUtc) return; // 自己刚写的
        _lastWriteUtc = stamp;

        var message = _dirty
            ? "文件已被外部修改。\n重新加载会丢弃当前未保存的修改，继续吗？"
            : "文件已被外部修改，重新加载？";
        var result = MessageBox.Show(this, message, Product, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) _ = ReloadAsync(path);
    }

    async Task ReloadAsync(string path)
    {
        if (_kernel is null) return;
        try
        {
            var doc = TextFile.Read(path);
            _doc = doc;
            await _kernel.SendAsync("setMarkdown", new JsonObject
            {
                ["md"] = doc.Text,
                ["baseDir"] = Path.GetDirectoryName(path)!,
            }, TimeSpan.FromSeconds(20));
            await _kernel.SendAsync("markClean");
            _dirty = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            Log.Error("reload failed", ex);
        }
    }

    // ---------------------------------------------------------------- 关闭

    async Task CloseWindowAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _closingConfirmed = true;
        Close();
    }

    async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        var name = _path is null ? "未命名" : Path.GetFileName(_path);
        var result = MessageBox.Show(this, $"「{name}」有未保存的修改，保存吗？", Product,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            await SaveAsync(false);
            return !_dirty;
        }
        return true;
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        e.Cancel = true;
        _ = CloseWindowAsync();
    }

    // ---------------------------------------------------------------- 状态与菜单

    void UpdateTitle()
    {
        var name = _path is null ? "未命名" : Path.GetFileName(_path);
        Title = $"{name}{(_dirty ? " *" : "")} — {Product}";
        UpdateState();
    }

    void UpdateState()
    {
        StateText.Text = !_ready ? "加载中…" : _dirty ? "未保存" : "已保存";
        if (_path is not null) StateText.Text += " · " + Path.GetFileName(_path);
    }

    void UpdateRecentMenu()
    {
        _recentMenu.Items.Clear();
        if (_settings.Recent.Count == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "（空）", IsEnabled = false });
            return;
        }
        foreach (var recent in _settings.Recent)
        {
            var item = new MenuItem { Header = Path.GetFileName(recent), ToolTip = recent };
            item.Click += (_, _) => App.Current.OpenDocument(recent);
            _recentMenu.Items.Add(item);
        }
        _recentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "清空最近列表" };
        clear.Click += (_, _) => { _settings.Recent.Clear(); _settings.Save(); UpdateRecentMenu(); };
        _recentMenu.Items.Add(clear);
    }

    MenuItem Item(string header, RoutedEventHandler handler, string? gesture = null, bool checkable = false)
    {
        var item = new MenuItem { Header = header, IsCheckable = checkable };
        if (gesture is not null) item.InputGestureText = gesture;
        item.Click += handler;
        return item;
    }

    void BuildMenu()
    {
        var file = new MenuItem { Header = "文件(_F)" };
        file.Items.Add(Item("新建", (_, _) => _ = NewDocumentAsync(), "Ctrl+N"));
        file.Items.Add(Item("打开…", (_, _) => _ = OpenViaDialogAsync(), "Ctrl+O"));
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(Item("保存", (_, _) => _ = SaveAsync(false), "Ctrl+S"));
        file.Items.Add(Item("另存为…", (_, _) => _ = SaveAsync(true), "Ctrl+Shift+S"));
        file.Items.Add(new Separator());
        file.Items.Add(Item("关闭窗口", (_, _) => _ = CloseWindowAsync(), "Ctrl+W"));
        file.Items.Add(Item("退出", (_, _) => App.Current.ShutdownApp()));

        var edit = new MenuItem { Header = "编辑(_E)" };
        edit.Items.Add(Item("撤销", (_, _) => _ = RunOpAsync("undo"), "Ctrl+Z"));
        edit.Items.Add(Item("重做", (_, _) => _ = RunOpAsync("redo"), "Ctrl+Y"));
        edit.Items.Add(new Separator());
        edit.Items.Add(Item("查找…", (_, _) => _ = RunOpAsync("openFindBar"), "Ctrl+F"));
        _sourceMenu.Click += (_, _) => _ = RunOpAsync("toggleSource");
        edit.Items.Add(_sourceMenu);

        var view = new MenuItem { Header = "视图(_V)" };
        var theme = new MenuItem { Header = "主题" };
        theme.Items.Add(Item("跟随系统", (_, _) => _ = SetThemeAsync("system"), checkable: true));
        theme.Items.Add(Item("浅色", (_, _) => _ = SetThemeAsync("light"), checkable: true));
        theme.Items.Add(Item("深色", (_, _) => _ = SetThemeAsync("dark"), checkable: true));
        view.Items.Add(theme);
        var zoom = new MenuItem { Header = "缩放" };
        zoom.Items.Add(Item("放大", (_, _) => _ = ZoomByAsync(0.1), "Ctrl++"));
        zoom.Items.Add(Item("缩小", (_, _) => _ = ZoomByAsync(-0.1), "Ctrl+-"));
        zoom.Items.Add(Item("重置", (_, _) => _ = SetZoomAsync(1.0), "Ctrl+0"));
        view.Items.Add(zoom);
        _minimapMenu.Click += (_, _) => _ = ToggleMinimapAsync();
        view.Items.Add(_minimapMenu);

        var help = new MenuItem { Header = "帮助(_H)" };
        help.Items.Add(Item("打开日志目录", (_, _) =>
        {
            Directory.CreateDirectory(AppInfo.UserDataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppInfo.UserDataDirectory) { UseShellExecute = true });
        }));
        var about = $"MDPreview for Windows\n协议 v{AppInfo.ProtocolVersion} · 内核端口 {AppInfo.WebDirectory}";
        help.Items.Add(Item("关于", (_, _) => MessageBox.Show(this, about, Product,
            MessageBoxButton.OK, MessageBoxImage.Information)));

        MainMenu.Items.Add(file);
        MainMenu.Items.Add(edit);
        MainMenu.Items.Add(view);
        MainMenu.Items.Add(help);
        UpdateRecentMenu();
        UpdateMenuChecks();
    }

    void UpdateMenuChecks()
    {
        var theme = (MenuItem)MainMenu.Items[2];
        var themeMenu = (MenuItem)theme.Items[0];
        var expected = _settings.Theme switch { "dark" => "深色", "light" => "浅色", _ => "跟随系统" };
        foreach (var entry in themeMenu.Items.OfType<MenuItem>())
            entry.IsChecked = (string)entry.Header == expected;
        _minimapMenu.IsChecked = _settings.MinimapVisible;
    }
}
