using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MDPreview.Win.Bridge;
using MDPreview.Win.Io;
using MDPreview.Win.Settings;
using Microsoft.Web.WebView2.Core;
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
    readonly MenuItem _revealMenu = new() { Header = "在资源管理器中显示" };
    readonly MenuItem _sourceMenu = new() { Header = "源码模式", IsCheckable = true };
    readonly MenuItem _minimapMenu = new() { Header = "显示文档缩略图", IsCheckable = true };
    readonly MenuItem _drawerMenu = new() { Header = "文件抽屉", IsCheckable = true };
    readonly MenuItem _languageMenu = new() { Header = "代码语言" };
    readonly MenuItem _formatMenu = new() { Header = "格式(_O)" };
    readonly MenuItem _tableMenu = new() { Header = "表格" };
    readonly MenuItem _lineNumbersMenu = new() { Header = "显示行号", IsCheckable = true };
    readonly List<MenuItem> _themeItems = [];
    bool _inTable;

    static readonly object DrawerPlaceholder = new();
    string? _drawerRoot;
    string? _directoryRoot;
    DateTime _lastDrawerRefresh;

    /// <summary>超过该字节数的代码文件不启用语法高亮（纯文本渲染，避免卡顿）。</summary>
    const long CodeHighlightByteLimit = 2_000_000;

    static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mdx", ".txt", ".text",
    };

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".tif", ".tiff", ".heic", ".avif",
    };

    /// <summary>代码文件 = 非 Markdown 的可开文件；只读视图，不写盘。</summary>
    bool IsCodeDocument => _path is not null && !MarkdownExtensions.Contains(Path.GetExtension(_path));

    /// <summary>目录模式：没有文档，只有抽屉导航（CLI 目录参数 / 打开文件夹）。</summary>
    bool IsDirectoryMode => _directoryRoot is not null;
    bool _codeHighlightAllowed = true;
    bool _codeLoaded;

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
        Activated += (_, _) =>
        {
            UpdateRecentMenu();
            RefreshDrawerOnActivate();
        };
        ApplyDrawerState();
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
            _host.DroppedImage += OnDroppedImage;
            await _host.InitializeAsync();
            _kernel = _host.Client;
            InitializeContextMenu(_host.Core);

            var pending = _initialPath ?? _deferredPath;
            if (pending is not null && Directory.Exists(pending)) await OpenDirectoryInitialAsync(pending);
            else if (pending is not null && File.Exists(pending)) await OpenInitialAsync(pending);
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
        var isCode = !MarkdownExtensions.Contains(Path.GetExtension(path));
        var doc = TextFile.Read(path);
        _doc = doc;
        _codeLoaded = false;
        await SendInitAsync(isCode ? "" : doc.Text, path);
        _lastWriteUtc = File.GetLastWriteTimeUtc(path);
        _settings.AddRecent(path);
        UpdateTitle();
        UpdateRecentMenu();
        StartWatcher(path);
        Log.Info("opened " + path);
    }

    /// <summary>目录模式：空编辑器 + 抽屉导航（CLI 目录参数 / 打开文件夹）。</summary>
    async Task OpenDirectoryInitialAsync(string directory)
    {
        _directoryRoot = directory;
        _doc = null;
        _dirty = false;
        await SendInitAsync("", null);
        _settings.DrawerVisible = true;
        _settings.Save();
        ApplyDrawerState();
        UpdateTitle();
        LoadDrawerRoot();
        Log.Info("opened folder " + directory);
    }

    /// <summary>「打开文件夹」：新窗口进入目录模式（不覆盖当前文档）。</summary>
    async Task OpenFolderViaDialogAsync()
    {
        var dialog = new OpenFolderDialog { Title = "打开文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        App.Current.OpenDocument(dialog.FolderName);
        await Task.CompletedTask;
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

        if (_settings.DrawerVisible) LoadDrawerRoot();
    }

    /// <summary>
    /// 代码文件装载：只读高亮视图（不进 ProseMirror、不产生脏基线与 changed）。
    /// 超过字节上限的文件降级纯文本，状态栏标注「纯文本」。
    /// </summary>
    async Task LoadCodeAsync(string path)
    {
        if (_kernel is null) return;
        var doc = TextFile.Read(path);
        _doc = doc;
        _path = path;

        var language = CodeLanguages.IdForPath(path);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(doc.Text);
        _codeHighlightAllowed = bytes <= CodeHighlightByteLimit;
        if (!_codeHighlightAllowed) language = CodeLanguages.PlainText;

        await _kernel.SendAsync("setCode", new JsonObject
        {
            ["text"] = doc.Text,
            ["language"] = language,
            ["allowHighlight"] = _codeHighlightAllowed,
            ["lineNumbers"] = _settings.CodeLineNumbers,
        }, TimeSpan.FromSeconds(20));

        var lines = doc.Text.Length == 0 ? 0 : doc.Text.Split('\n').Length;
        StatsText.Text = $"{lines} 行 · {doc.Text.Length} 字符";
        _codeLoaded = true;
        _dirty = false;
        UpdateTitle();
        UpdateState();

        // 自检用一行：代码视图的真实渲染结果（行数/语言/是否高亮）进日志，便于无界面核验
        try
        {
            var info = await _kernel.SendAsync("codeInfo", null, TimeSpan.FromSeconds(5));
            Log.Info($"code view {info?.ToJsonString()}");
        }
        catch (Exception ex)
        {
            Log.Warn("codeInfo failed: " + ex.Message);
        }

        Log.Info($"opened code {path} language={language} highlight={_codeHighlightAllowed}");
    }

    // ---------------------------------------------------------------- 打开 / 保存

    /// <summary>
    /// 在当前窗口覆盖打开文档：脏文档先走与关闭同一套「保存/不保存/取消」确认。
    /// <paramref name="reanchor"/> 与 macOS <c>openDocument(at:reanchor:)</c> 同义——抽屉双击传
    /// false（保持锚点），Ctrl+O 等外部来源用默认 true（重锚到文件所在目录）。
    /// </summary>
    public async Task OpenPathAsync(string path, bool reanchor = true)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "文件不存在：" + path, Product, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_kernel is null) { _deferredPath = path; return; }
        if (!await ConfirmDiscardAsync()) return;

        _directoryRoot = null; // 打开文件即退出目录模式（与 macOS openDocument 同口径）
        try
        {
            if (!MarkdownExtensions.Contains(Path.GetExtension(path)))
            {
                await LoadCodeAsync(path);
                _lastWriteUtc = File.GetLastWriteTimeUtc(path);
                _settings.AddRecent(path);
                UpdateRecentMenu();
                StartWatcher(path);
                UpdateDrawerForOpen(path, reanchor);
                return;
            }

            var doc = TextFile.Read(path);
            _doc = doc;
            _path = path;
            _codeLoaded = false;
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
            UpdateDrawerForOpen(path, reanchor);
            Log.Info("opened " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开失败：" + ex.Message, Product, MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error("open failed", ex);
        }
    }

    /// <summary>打开文档后的抽屉刷新：按来源决定锚点，再重载树并选中当前文档。</summary>
    void UpdateDrawerForOpen(string path, bool reanchor)
    {
        var root = DrawerAnchor.RootAfterOpen(_drawerRoot, path, reanchor);
        if (!string.Equals(root, _drawerRoot, StringComparison.OrdinalIgnoreCase))
        {
            SetDrawerRoot(root); // 锚点变了：即使抽屉暂时隐藏也更新，展开时才是正确根
            return;
        }
        if (_settings.DrawerVisible) ReloadDrawerTree();
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
        if (IsCodeDocument)
        {
            MessageBox.Show(this, "代码文件是只读视图，不写回磁盘。", Product,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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
                // 代码视图要等 ready：setCode 在 ready 之前一律返回 ok:false（内核契约）
                if (!_codeLoaded && _path is not null && IsCodeDocument) _ = LoadCodeAsync(_path);
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
            case "selinfo":
                _inTable = payload["inTable"]?.GetValue<bool>() ?? false;
                UpdateDocumentMenuState();
                break;
            case "jserror":
                Log.Warn("kernel js error: " + payload["message"]);
                break;
        }
    }

    Task<JsonNode?> HandleKernelRequest(string op, JsonObject payload) => op switch
    {
        "resolveResources" => Task.FromResult<JsonNode?>(ResolveResources(payload)),
        "importImage" => Task.FromResult<JsonNode?>(ImportImage(payload)),
        _ => throw new KernelException("unknown_op", $"壳未实现请求 {op}"),
    };

    /// <summary>
    /// 粘贴/拖入图片：壳落盘到 <c>resource/&lt;文档名&gt;/</c>，回传可显示的资源 URL
    /// （保存时由内核按 PathPolicy 还原成相对路径）。失败只影响这一次粘贴。
    /// </summary>
    JsonObject ImportImage(JsonObject payload)
    {
        if (_path is null)
        {
            PromptSaveBeforeImage();
            return new JsonObject { ["ok"] = false };
        }

        try
        {
            var kind = payload["kind"]?.GetValue<string>()
                       ?? (payload["data"] is null ? "file" : "blob");
            ImportedImage imported;
            if (kind == "blob")
            {
                var base64 = payload["data"]?.GetValue<string>() ?? "";
                if (base64.Length == 0) throw new ImageImportException("剪贴板里没有图片数据");
                imported = ImageStore.ImportBlob(Convert.FromBase64String(base64),
                    payload["mime"]?.GetValue<string>(), _path);
            }
            else
            {
                var raw = payload["path"]?.GetValue<string>() ?? "";
                imported = ImageStore.ImportFile(ImageStore.PathFromText(raw), _path);
            }

            Log.Info($"image imported {imported.FileName}");
            return new JsonObject { ["ok"] = true, ["src"] = ImageStore.ToResourceUrl(imported.Path) };
        }
        catch (Exception ex)
        {
            Log.Warn("image import failed: " + ex.Message);
            MessageBox.Show(this, ImageStore.DescribeFailure(ex), "导入图片失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return new JsonObject { ["ok"] = false };
        }
    }

    void PromptSaveBeforeImage() => MessageBox.Show(this,
        "请先保存文档。\n插入图片需要先保存文档，图片将存入文档旁的 resource/ 目录。",
        Product, MessageBoxButton.OK, MessageBoxImage.Information);

    async Task InsertImageFromDialogAsync()
    {
        if (_kernel is null) return;
        if (_path is null) { PromptSaveBeforeImage(); return; }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "插入图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.tif;*.tiff;*.heic;*.avif|所有文件|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        await InsertImageFileAsync(dialog.FileName);
    }

    async Task InsertImageFileAsync(string path)
    {
        if (_kernel is null) return;
        if (_path is null) { PromptSaveBeforeImage(); return; }
        try
        {
            var imported = ImageStore.ImportFile(path, _path);
            await _kernel.SendAsync("insertImage", new JsonObject
            {
                ["src"] = ImageStore.ToResourceUrl(imported.Path),
                ["alt"] = "",
                ["title"] = "",
            });
            Log.Info($"image inserted {imported.FileName}");
        }
        catch (Exception ex)
        {
            Log.Warn("image insert failed: " + ex.Message);
            MessageBox.Show(this, ImageStore.DescribeFailure(ex), "导入图片失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// 拖入文件：WebView2 外部拖放关闭后路径由 WPF Drop 直接拿到——图片走同一落盘/插入链路，
    /// `.md` 开新窗口，目录进入目录模式；不再经页面上传 base64。
    /// </summary>
    async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                App.Current.OpenDocument(path);
                continue;
            }
            if (!File.Exists(path)) continue;
            var extension = Path.GetExtension(path);
            if (ImageExtensions.Contains(extension))
            {
                if (!IsCodeDocument && !IsDirectoryMode) await InsertImageFileAsync(path);
                continue;
            }
            if (MarkdownExtensions.Contains(extension)) App.Current.OpenDocument(path);
        }
    }

    /// <summary>关闭时清理 resource 目录里未被引用的图片（引用集合由内核给）。</summary>
    async Task CollectUnreferencedImagesAsync()
    {
        if (_kernel is null || _path is null || _dirty) return;
        try
        {
            var result = await _kernel.SendAsync("getResourceRefs", null, TimeSpan.FromSeconds(5));
            var referenced = new List<string>();
            if (result?["paths"] is JsonArray paths)
                foreach (var item in paths)
                    if (item?.GetValue<string>() is { Length: > 0 } value) referenced.Add(value);

            var deleted = ImageStore.CleanUnreferenced(_path, referenced);
            if (deleted.Count > 0) Log.Info("image gc deleted: " + string.Join(", ", deleted));
        }
        catch (Exception ex)
        {
            Log.Warn("image gc skipped: " + ex.Message);
        }
    }

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
            "openFolder" => OpenFolderViaDialogAsync(),
            "save" => SaveAsync(false),
            "saveAs" => SaveAsync(true),
            "close" => CloseWindowAsync(),
            "find" => RunOpAsync("openFindBar"),
            "findNext" => RunOpAsync("findNext"),
            "findPrev" => RunOpAsync("findPrev"),
            "print" => PrintAsync(),
            "toggleSource" => RunOpAsync("toggleSource"),
            "toggleDrawer" => ToggleDrawerAsync(),
            "zoomIn" => ZoomByAsync(0.1),
            "zoomOut" => ZoomByAsync(-0.1),
            "zoomReset" => SetZoomAsync(1.0),
            _ => Task.CompletedTask,
        };
    }

    Task ToggleDrawerAsync()
    {
        ToggleDrawer();
        return Task.CompletedTask;
    }

    void OnDroppedImage(string mime, string base64)
    {
        if (_kernel is null || string.IsNullOrEmpty(base64)) return;
        _ = InsertDroppedImageAsync(mime, base64);
    }

    /// <summary>拖入的图片：壳落盘 → 内核 insertImage（与粘贴同一条落盘/插入路径）。</summary>
    async Task InsertDroppedImageAsync(string mime, string base64)
    {
        if (_path is null) { PromptSaveBeforeImage(); return; }
        try
        {
            var imported = ImageStore.ImportBlob(Convert.FromBase64String(base64), mime, _path);
            await _kernel!.SendAsync("insertImage", new JsonObject
            {
                ["src"] = ImageStore.ToResourceUrl(imported.Path),
                ["alt"] = "",
                ["title"] = "",
            });
            Log.Info($"image dropped {imported.FileName}");
        }
        catch (Exception ex)
        {
            Log.Warn("image drop failed: " + ex.Message);
            MessageBox.Show(ImageStore.DescribeFailure(ex), "导入图片失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- 文件抽屉

    void OnDrawerToggleClick(object sender, RoutedEventArgs e) => ToggleDrawer();

    void ToggleDrawer()
    {
        _settings.DrawerVisible = !_settings.DrawerVisible;
        _settings.Save();
        ApplyDrawerState();
        if (_settings.DrawerVisible && _drawerRoot is null) LoadDrawerRoot();
    }

    void ApplyDrawerState()
    {
        var visible = _settings.DrawerVisible;
        _drawerMenu.IsChecked = visible;
        DrawerToggleButton.ToolTip = visible ? "隐藏文件抽屉（Ctrl+\\）" : "显示文件抽屉（Ctrl+\\）";
        DrawerToggleButton.Background = visible
            ? (Brush)FindResource("ChromeBorder")
            : Brushes.Transparent;
        DrawerPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DrawerSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DrawerColumn.Width = visible ? new GridLength(Math.Clamp(_settings.DrawerWidth, 180, 520)) : new GridLength(0);
        SplitColumn.Width = visible ? new GridLength(4) : new GridLength(0);
    }

    void LoadDrawerRoot()
    {
        SetDrawerRoot(_directoryRoot ?? FileTree.RootFor(_path));
    }

    /// <summary>切锚点/刷新树：抽屉根目录的唯一写入口。</summary>
    void SetDrawerRoot(string directory)
    {
        if (!Directory.Exists(directory)) return;
        _drawerRoot = directory;
        UpdateDrawerRootLabel();
        ReloadDrawerTree();
    }

    void UpdateDrawerRootLabel()
    {
        if (_drawerRoot is null) return;
        DrawerRootText.Text = DrawerAnchor.DisplayName(_drawerRoot);
        DrawerRootText.ToolTip = _drawerRoot;
    }

    void ReloadDrawerTree()
    {
        if (_drawerRoot is null) return;
        DrawerTree.Items.Clear();
        foreach (var entry in FileTree.Enumerate(_drawerRoot)) DrawerTree.Items.Add(BuildDrawerNode(entry));
        SelectCurrentDocumentInDrawer();
    }

    /// <summary>窗口重新激活时刷新根层（节流 2 s，避免菜单/弹窗引起的抖动）。</summary>
    void RefreshDrawerOnActivate()
    {
        if (_drawerRoot is null || !_settings.DrawerVisible) return;
        var now = DateTime.UtcNow;
        if ((now - _lastDrawerRefresh).TotalSeconds < 2) return;
        _lastDrawerRefresh = now;
        ReloadDrawerTree();
    }

    TreeViewItem BuildDrawerNode(FileEntry entry)
    {
        var item = new TreeViewItem
        {
            Tag = entry,
            Header = DrawerHeader(entry),
            Padding = new Thickness(2, 1, 2, 1),
            FontWeight = entry.Path == _path ? FontWeights.SemiBold : FontWeights.Normal,
        };
        if (entry.IsDirectory)
        {
            // 懒加载：先放占位行，展开时才取下一层（大目录不一次扫盘）
            item.Items.Add(new TreeViewItem { Tag = DrawerPlaceholder });
            item.Expanded += OnDrawerDirectoryExpanded;
        }
        return item;
    }

    static TextBlock DrawerHeader(FileEntry entry) => new()
    {
        Text = (entry.IsDirectory ? "▸ " : "  ") + entry.Name,
        TextTrimming = TextTrimming.CharacterEllipsis,
        ToolTip = entry.Path,
    };

    void OnDrawerDirectoryExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not FileEntry entry || !entry.IsDirectory) return;
        if (item.Items.Count != 1 || !ReferenceEquals((item.Items[0] as TreeViewItem)?.Tag, DrawerPlaceholder)) return;

        item.Items.Clear();
        foreach (var child in FileTree.Enumerate(entry.Path)) item.Items.Add(BuildDrawerNode(child));
    }

    void SelectCurrentDocumentInDrawer()
    {
        if (_path is null) return;
        foreach (var item in DrawerTree.Items.OfType<TreeViewItem>())
        {
            if (item.Tag is FileEntry entry && !entry.IsDirectory &&
                string.Equals(entry.Path, _path, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                item.BringIntoView();
                return;
            }
        }
    }

    async void OnDrawerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DrawerTree.SelectedItem is not TreeViewItem item || item.Tag is not FileEntry entry) return;
        if (entry.IsDirectory)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }
        if (entry.Path == _path) return;
        e.Handled = true;
        // 同窗覆盖且保持锚点（与 macOS FileDrawerView.onOpenFile → openDocument(reanchor:false) 同口径）
        await OpenPathAsync(entry.Path, reanchor: false);
    }

    void OnDrawerUpClick(object sender, RoutedEventArgs e)
    {
        if (_drawerRoot is null) return;
        var parent = Path.GetDirectoryName(_drawerRoot);
        if (string.IsNullOrEmpty(parent)) return;
        SetDrawerRoot(parent);
    }

    void OnDrawerHomeClick(object sender, RoutedEventArgs e)
    {
        SetDrawerRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>锚点菜单：盘符根 → 当前目录的祖先链，选中即重锚抽屉。</summary>
    void OnDrawerAnchorClick(object sender, RoutedEventArgs e)
    {
        if (_drawerRoot is null) return;
        var menu = new ContextMenu { PlacementTarget = DrawerAnchorButton };
        foreach (var directory in DrawerAnchor.Chain(_drawerRoot))
        {
            var item = new MenuItem
            {
                Header = DrawerAnchor.DisplayName(directory),
                IsCheckable = true,
                IsChecked = string.Equals(directory, _drawerRoot, StringComparison.OrdinalIgnoreCase),
                ToolTip = directory,
            };
            item.Click += (_, _) => SetDrawerRoot(directory);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void OnDrawerSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DrawerColumn.Width.IsAbsolute && DrawerColumn.Width.Value > 0)
        {
            _settings.DrawerWidth = DrawerColumn.Width.Value;
            _settings.Save();
        }
    }

    // ---------------------------------------------------------------- 诊断入口（--diag）

    /// <summary>等内核 ready 且首篇文档装载完成（诊断子命令用，最多 ~30 s）。</summary>
    public async Task WaitReadyAsync()
    {
        for (var i = 0; i < 300 && !_ready; i++) await Task.Delay(100);
        for (var i = 0; i < 100 && IsCodeDocument && !_codeLoaded; i++) await Task.Delay(100);
        await Task.Delay(200);
    }

    public async Task<string> DiagCodeInfoAsync()
    {
        if (_kernel is null) return "{}";
        var info = await _kernel.SendAsync("codeInfo", null, TimeSpan.FromSeconds(10));
        var minimap = await _kernel.SendAsync("minimapInfo", null, TimeSpan.FromSeconds(5));
        return $"{info?.ToJsonString() ?? "{}"}\nminimap={minimap?.ToJsonString() ?? "{}"}";
    }

    /// <summary>会话自检：状态 / 缩略图 / 源码模式往返 / 查找栏往返。</summary>
    public async Task<string> DiagStateAsync()
    {
        if (_kernel is null) return "{}";
        var lines = new List<string>();
        async Task<string> Op(string op, JsonObject? payload = null)
        {
            var answer = await _kernel.SendAsync(op, payload, TimeSpan.FromSeconds(10));
            return answer?.ToJsonString() ?? "{}";
        }

        lines.Add("state      " + await Op("getState"));
        lines.Add("minimap    " + await Op("minimapInfo"));
        lines.Add("findInfo   " + await Op("findInfo"));

        if (!IsCodeDocument)
        {
            lines.Add("source on  " + await Op("toggleSource"));
            lines.Add("source off " + await Op("toggleSource"));
            lines.Add("find open  " + await Op("openFindBar"));
            lines.Add("find after " + await Op("findInfo"));
            lines.Add("find close " + await Op("closeFindBar"));
        }
        return string.Join('\n', lines);
    }

    public async Task<int> DiagExportHtmlAsync(string target)
    {
        if (_kernel is null) return 0;
        var answer = await _kernel.SendAsync("exportStandaloneHTML", null, TimeSpan.FromSeconds(30));
        var html = answer?["html"]?.GetValue<string>() ?? "";
        await File.WriteAllTextAsync(target, html, new System.Text.UTF8Encoding(false));
        Log.Info($"diag exported html {target} bytes={html.Length}");
        return html.Length;
    }

    public async Task<bool> DiagExportPdfAsync(string target)
    {
        if (Web.CoreWebView2 is null) return false;
        var settings = Web.CoreWebView2.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.MarginTop = 0.4; settings.MarginBottom = 0.4;
        settings.MarginLeft = 0.4; settings.MarginRight = 0.4;
        var ok = await Web.CoreWebView2.PrintToPdfAsync(target, settings);
        Log.Info($"diag exported pdf {target} ok={ok}");
        return ok;
    }

    // ---------------------------------------------------------------- 导出

    /// <summary>导出 PDF：直接用 WebView2 的打印到 PDF（所见即所得，含图片与公式）。</summary>
    async Task ExportPdfAsync()
    {
        if (_kernel is null || Web.CoreWebView2 is null) return;
        if (IsCodeDocument)
        {
            MessageBox.Show(this, "代码文件不提供 PDF 导出。", Product, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 PDF",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            FileName = (_path is null ? "未命名" : Path.GetFileNameWithoutExtension(_path)) + ".pdf",
            InitialDirectory = _path is null ? _settings.LastFolder : Path.GetDirectoryName(_path),
            DefaultExt = ".pdf",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var settings = Web.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginTop = 0.4; settings.MarginBottom = 0.4;
            settings.MarginLeft = 0.4; settings.MarginRight = 0.4;

            var result = await Web.CoreWebView2.PrintToPdfAsync(dialog.FileName, settings);
            if (result) Log.Info("exported pdf " + dialog.FileName);
            else MessageBox.Show(this, "PDF 导出被取消或失败。", Product, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("pdf export failed", ex);
            MessageBox.Show(this, "PDF 导出失败：" + ex.Message, Product, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>导出单文件 HTML：样式由内核内联，图片保持相对路径（与 Markdown 同目录布局一致）。</summary>
    async Task ExportHtmlAsync()
    {
        if (_kernel is null) return;
        if (IsCodeDocument)
        {
            MessageBox.Show(this, "代码文件不提供 HTML 导出。", Product, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 HTML",
            Filter = "HTML 文件 (*.html)|*.html",
            FileName = (_path is null ? "未命名" : Path.GetFileNameWithoutExtension(_path)) + ".html",
            InitialDirectory = _path is null ? _settings.LastFolder : Path.GetDirectoryName(_path),
            DefaultExt = ".html",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var answer = await _kernel.SendAsync("exportStandaloneHTML", null, TimeSpan.FromSeconds(20));
            var html = answer?["html"]?.GetValue<string>() ?? "";
            if (html.Length == 0)
            {
                MessageBox.Show(this, "HTML 导出失败（内核返回空内容）。", Product, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await File.WriteAllTextAsync(dialog.FileName, html, new System.Text.UTF8Encoding(false));
            Log.Info("exported html " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("html export failed", ex);
            MessageBox.Show(this, "HTML 导出失败：" + ex.Message, Product, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------- 代码语言

    void BuildLanguageMenu(MenuItem parent)
    {
        parent.Items.Clear();
        var plain = new MenuItem { Header = "纯文本", IsCheckable = true };
        plain.Click += (_, _) => _ = SetCodeLanguageAsync(CodeLanguages.PlainText);
        parent.Items.Add(plain);
        parent.Items.Add(new Separator());

        foreach (var language in CodeLanguages.All)
        {
            var id = language.Id;
            var item = new MenuItem { Header = language.DisplayName, IsCheckable = true };
            item.Click += (_, _) => _ = SetCodeLanguageAsync(id);
            parent.Items.Add(item);
        }
        if (CodeLanguages.All.Count == 0)
            parent.Items.Add(new MenuItem { Header = "（languages.json 缺失）", IsEnabled = false });
    }

    async Task SetCodeLanguageAsync(string id)
    {
        if (_kernel is null || !IsCodeDocument) return;
        try
        {
            await _kernel.SendAsync("setCodeLanguage", new JsonObject { ["id"] = id });
            Log.Info($"code language -> {id}");
        }
        catch (Exception ex)
        {
            Log.Warn("set code language failed: " + ex.Message);
        }
    }

    void UpdateLanguageMenuChecks()
    {
        var current = _path is null ? CodeLanguages.PlainText : CodeLanguages.IdForPath(_path);
        foreach (var item in _languageMenu.Items.OfType<MenuItem>())
        {
            var header = item.Header as string;
            item.IsChecked = header == CodeLanguages.DisplayName(current);
        }
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
            if (!MarkdownExtensions.Contains(Path.GetExtension(path)))
            {
                await LoadCodeAsync(path);
                UpdateTitle();
                return;
            }

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
        await CollectUnreferencedImagesAsync();
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
        if (IsDirectoryMode)
        {
            Title = $"{DrawerAnchor.DisplayName(_directoryRoot!)} — {Product}";
            UpdateDocumentMenuState();
            UpdateState();
            return;
        }
        var name = _path is null ? "未命名" : Path.GetFileName(_path);
        Title = $"{name}{(_dirty ? " *" : "")} — {Product}";
        UpdateDocumentMenuState();
        UpdateState();
    }

    void UpdateState()
    {
        if (IsDirectoryMode)
        {
            StateText.Text = "目录 · " + DrawerAnchor.DisplayName(_directoryRoot!);
            return;
        }
        if (IsCodeDocument && _ready)
        {
            StateText.Text = _path is null ? "只读" : "只读 · " + Path.GetFileName(_path);
            return;
        }
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
        file.Items.Add(Item("打开文件夹…", (_, _) => _ = OpenFolderViaDialogAsync(), "Ctrl+Shift+O"));
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(Item("保存", (_, _) => _ = SaveAsync(false), "Ctrl+S"));
        file.Items.Add(Item("另存为…", (_, _) => _ = SaveAsync(true), "Ctrl+Shift+S"));
        _revealMenu.Click += (_, _) => RevealDocument();
        file.Items.Add(_revealMenu);
        file.Items.Add(new Separator());
        file.Items.Add(Item("导出 PDF…", (_, _) => _ = ExportPdfAsync()));
        file.Items.Add(Item("导出 HTML…", (_, _) => _ = ExportHtmlAsync()));
        file.Items.Add(Item("打印…", (_, _) => PrintDocument(), "Ctrl+P"));
        file.Items.Add(new Separator());
        file.Items.Add(Item("关闭窗口", (_, _) => _ = CloseWindowAsync(), "Ctrl+W"));
        file.Items.Add(Item("退出", (_, _) => App.Current.ShutdownApp()));

        var edit = new MenuItem { Header = "编辑(_E)" };
        AddGroup(edit, EditorCommands.UndoGroup, separatorBefore: false);
        AddGroup(edit, EditorCommands.ClipboardGroup);
        AddGroup(edit, EditorCommands.FindGroup);

        var view = new MenuItem { Header = "视图(_V)" };
        var zoom = new MenuItem { Header = "缩放" };
        zoom.Items.Add(Item("放大", (_, _) => _ = ZoomByAsync(0.1), "Ctrl++"));
        zoom.Items.Add(Item("缩小", (_, _) => _ = ZoomByAsync(-0.1), "Ctrl+-"));
        zoom.Items.Add(Item("重置", (_, _) => _ = SetZoomAsync(1.0), "Ctrl+0"));
        view.Items.Add(zoom);
        view.Items.Add(new Separator());
        _sourceMenu.Click += (_, _) => _ = RunOpAsync("toggleSource");
        view.Items.Add(_sourceMenu);
        _lineNumbersMenu.Click += (_, _) => ToggleLineNumbers();
        view.Items.Add(_lineNumbersMenu);
        _minimapMenu.Click += (_, _) => _ = ToggleMinimapAsync();
        view.Items.Add(_minimapMenu);
        _drawerMenu.Click += (_, _) => ToggleDrawer();
        view.Items.Add(_drawerMenu);
        view.Items.Add(new Separator());
        BuildLanguageMenu(_languageMenu);
        _languageMenu.SubmenuOpened += (_, _) => UpdateLanguageMenuChecks();
        view.Items.Add(_languageMenu);
        var theme = new MenuItem { Header = "主题" };
        foreach (var (header, value) in new[] { ("跟随系统", "system"), ("浅色", "light"), ("深色", "dark") })
        {
            var item = Item(header, (_, _) => _ = SetThemeAsync(value), checkable: true);
            _themeItems.Add(item);
            theme.Items.Add(item);
        }
        view.Items.Add(theme);

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
        MainMenu.Items.Add(BuildFormatMenu());
        MainMenu.Items.Add(view);
        MainMenu.Items.Add(help);
        UpdateRecentMenu();
        UpdateMenuChecks();
    }

    MenuItem Item(EditorCommand command)
    {
        var item = new MenuItem { Header = command.Title };
        if (command.Gesture is not null) item.InputGestureText = command.Gesture;
        item.Click += (_, _) => ExecuteEditorCommand(command);
        return item;
    }

    void AddGroup(ItemsControl parent, IReadOnlyList<EditorCommand> group, bool separatorBefore = true)
    {
        if (group.Count == 0) return;
        if (separatorBefore && parent.Items.Count > 0) parent.Items.Add(new Separator());
        foreach (var command in group) parent.Items.Add(Item(command));
    }

    MenuItem BuildFormatMenu()
    {
        _formatMenu.Items.Clear();
        AddGroup(_formatMenu, EditorCommands.InlineFormatGroup, separatorBefore: false);
        AddGroup(_formatMenu, EditorCommands.HeadingGroup);
        AddGroup(_formatMenu, EditorCommands.BlockGroup);
        AddGroup(_formatMenu, EditorCommands.InsertBlockGroup);

        _tableMenu.Items.Clear();
        foreach (var command in EditorCommands.TableInsertGroup) _tableMenu.Items.Add(Item(command));
        _tableMenu.Items.Add(new Separator());
        foreach (var command in EditorCommands.TableDeleteGroup) _tableMenu.Items.Add(Item(command));
        _formatMenu.Items.Add(new Separator());
        _formatMenu.Items.Add(_tableMenu);
        _formatMenu.Items.Add(Item(EditorCommands.InsertHr));
        _formatMenu.Items.Add(new Separator());
        _formatMenu.Items.Add(Item(EditorCommands.InsertImage));
        return _formatMenu;
    }

    void ToggleLineNumbers()
    {
        if (!IsCodeDocument) return;
        _settings.CodeLineNumbers = !_settings.CodeLineNumbers;
        _settings.Save();
        App.Current.ApplyCodeLineNumbersToAll(_settings.CodeLineNumbers);
    }

    /// <summary>全局行号偏好变更：本窗口勾选态与代码视图即时同步（非代码窗口只更新勾选）。</summary>
    internal void ApplyCodeLineNumbers(bool visible)
    {
        _lineNumbersMenu.IsChecked = visible;
        if (IsCodeDocument && _kernel is not null)
            _ = _kernel.SendAsync("setCodeLineNumbers", new JsonObject { ["visible"] = visible });
    }

    void UpdateDocumentMenuState()
    {
        var code = IsCodeDocument;
        _formatMenu.IsEnabled = !code;
        _sourceMenu.IsEnabled = !code;
        _lineNumbersMenu.IsEnabled = code;
        _tableMenu.IsEnabled = !code && _inTable;
        _revealMenu.IsEnabled = _path is not null;
    }

    void UpdateMenuChecks()
    {
        var expected = _settings.Theme switch { "dark" => "深色", "light" => "浅色", _ => "跟随系统" };
        foreach (var entry in _themeItems) entry.IsChecked = (string)entry.Header == expected;
        _minimapMenu.IsChecked = _settings.MinimapVisible;
        _lineNumbersMenu.IsChecked = _settings.CodeLineNumbers;
        UpdateDocumentMenuState();
    }
}
