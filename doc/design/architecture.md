---
layer: design
module: global
status: draft
updated: 2026-11-05
---

# Windows 版系统架构与取舍

> 上游基线：`md-previewer@64ba91b`（内核 v2 + 组件拆分 + 文档缩略图已交付）。
> 本文是 Windows 侧的现行技术真相；壳选型确认后转 `status: current`。

## 1. 移植策略

**决策**：复用上游已完成的内核 v2，重写壳，契约不重定义。

- 内核：把上游 `web/` 以**快照 vendor** 方式复制为 `kernel/`（`src/kernel`、`page.js`、
  `test/`、`build.mjs`、`check-kernel.mjs`），来源 commit 记在 `kernel/SOURCE.md`；
  除一处 Windows 化补丁（§1.1）外不改内核源码。
- 壳：WPF（`net9.0-windows`）+ WebView2，负责窗口、菜单、文件 IO、编码、监听、抽屉、
  打印、诊断；实现 v2 客户端（41 op / 7 通知 / 2 请求 / 5 错误码）。
- 契约：沿用上游 v2 JSON 帧，**不新增 op、不改信封**；Windows 差异全部落在
  「宿主实现」与「资源 URL 方案」两处。

**理由**：上游 kernel-v2 的交付物里明确写了「Windows 参照物」（Node 套件 + FakeShell），
vendor 是它设计时预期的消费方式；R9（路径语义双头）已被上游关闭，重写壳不会重新打开它。

**不照搬**：macOS 的 `file://` 全盘读授权、WKWebView 消息处理器、`UserDefaults`、
AppKit 菜单——各自换成本平台等价物（§6、§7、§9）。

### 1.1 Windows 化补丁（唯一的内核改动）

上游把 `semantics/paths.js` 的 URL 组装写成 macOS 常量：`file://` + posix 分段；
`restoreForSave` 也硬编码 `file://` 前缀。Windows 必须把这两个常量参数化：

- `PathPolicy = { urlScheme, pathStyle }`：
  - `urlScheme`：`file`（macOS）或 `mdres`（Windows，§6.2）；
  - `pathStyle`：`posix` / `win32`（复用已有的 `normalizePath`/`joinBase` 分支）。
- 会话级注入：`init.opts` 带上策略，`document` 组件持有；`applyForLoad` 用壳返回的 URL，
  `restoreForSave` 按策略还原（否则保存会把 `mdres://` 写进文件——数据损坏）。
- 同口径覆盖 `collectResourceRefs`（GC 引用）与 `image-view` 的输入路径→URL。

补丁以 `kernel/PATCHES.md` 记录（文件、原因、测试），后续向上游提 PR；不在上游合并前
扩大补丁面。**不选**「壳侧把 `mdres://` 换回路径」：那等于在壳里重写 Markdown 文本，
违反硬约束并复活 R9。

## 2. 技术基线

| 项 | 选择 | 说明 |
|---|---|---|
| 运行时 | .NET 9（`net9.0-windows`） | 本机 SDK 9.0.320 已装 |
| 壳 UI | WPF + `Microsoft.Web.WebView2` | 待 Q1 确认；WinUI 3 为备选 |
| WebView2 | Runtime 156.0.4314.58（已装） | 缺 Runtime 时启动提示 |
| 内核 | 上游 v2（Milkdown 7.7 / ProseMirror） | vendor；esbuild 产物沿用上游 target |
| 编码 | UTF-8 / GB18030（`CodePagesEncodingProvider`） | 上游 FileSupport 同等口径 |
| 语言表 | `resources/languages.json`（唯一来源） | 内核与 C# 共同派生，禁止手抄 |

## 3. 进程、窗口、线程

- 单进程；默认单实例（命名管道把「打开路径」转给已有实例，`--new-instance` 逃生）。
- 一文档一窗口；无标签页；关闭即释放 WebView2 控制器。
- UI 线程：WPF 消息循环 + 每窗口一个 WebView2；内核请求（`resolveResources`、
  `importImage`）在 UI 线程处理，文件 IO 走 `Task` + `async`，不阻塞消息循环。
- 内核帧：`WebMessageReceived` 在 UI 线程到达，按 id 分发；`KernelClient` 维护 pending 表
  与超时（默认 10 s，与上游一致）。
- 退出：所有窗口关闭 → 若有未保存文档，逐个确认后退出；进程退出前等待 WebView2 释放。

## 4. 组件地图

壳（`src/MDPreview.Win`，单程序集，组件间只经接口/事件）：

| 组件 | 职责 |
|---|---|
| `CMP-app-shell` | 窗口、菜单、最近文件、单实例、主题/缩放、缩略图偏好 |
| `CMP-shell-bridge` | WebView2 宿主、`KernelClient`、虚拟主机、`mdres`、CSP、上下文菜单 |
| `CMP-document-io` | 读写、编码检测、原子写、尾换行、外部修改监听 |
| `CMP-drawer`（M2） | 目录树抽屉、懒加载、宽度拖拽 |
| `CMP-export`（M3） | PDF/HTML/打印 |
| `CMP-code-view`（M3） | 代码只读视图（语言/行号） |
| `CMP-diagnostics`（M3） | 自检、截图、日志 |

内核（`kernel/`，vendor，不改结构）：

| 层/组件 | 职责 |
|---|---|
| `transport` | 唯一宿主感知点（`__mdHostChannel` / `__mdKernel` / jserror） |
| `contract` | 信封、op 清单、注册表/握手、错误码 |
| `semantics` | 路径与 markdown 纯语义（Windows 补丁在此） |
| `shared` | 状态、通知/请求、端口注册表 |
| `components/{document,editor,code,find,output,media,minimap}` | 七个功能组件，互不 import，跨组件走 8 个端口 |

依赖方向：壳 → 帧 → transport → contract → components；`check-kernel.mjs` 守边。

## 5. 桥协议（v2）

- 数字：**41 host op、7 通知（`boot/ready/changed/stats/sourceMode/selinfo/jserror`）、
  2 内核请求（`resolveResources`、`importImage`）、5 错误码**（`unknown_op`、
  `bad_payload`、`not_ready`、`unsupported_version`、`internal`）。
- 帧：请求 `{v:2, op, id, payload}`，响应回显 `id` + `ok`/`error`，通知无 `id`。
- 握手：`init` 是 ready 前唯一允许的 op，必须带 `opts.protocolVersion`；不匹配即
  `unsupported_version` 且内核禁用（不静默降级）。
- Windows 实现：注入 20 行前导脚本（`__mdHostChannel.post` → `chrome.webview.postMessage`；
  `message` → `__mdKernel.receive`），壳侧 `PostWebMessageAsString` 发帧、
  `TryGetWebMessageAsString` 收帧——**不需要 `ExecuteScriptAsync`**。
- 两个请求必须由壳作答：`resolveResources` 自包含 `baseDir`（不猜当前目录），
  `importImage` 由壳落盘后回传路径；磁盘写入只发生在壳。

## 6. 资源加载与安全

### 6.1 页面来源

- 内核产物映射到虚拟主机 `https://mdpreview.local/`（`SetVirtualHostNameToFolderMapping`），
  页面与 bundle 从该源加载；不启本地 HTTP 服务。
- 关闭：DevTools、默认右键菜单、脚本对话框、下载、新窗口、权限请求（全部拒绝）。

### 6.2 图片与本地资源

- 方案：注册自定义 scheme `mdres`，`WebResourceRequested` 里按
  `mdres://local/<base64url(path)>` 读盘并以正确 MIME 返回。
- 为什么不用 `file://`：虚拟主机是 https 源，Chromium 拒绝 https 页面加载 `file://`
  子资源；换 `file://` 页面则要开 `--allow-file-access-from-files`（更宽的全局开关）。
- 壳返回给内核的 URL 即 `mdres://…`；保存时内核按 §1.1 还原为相对/绝对路径。
  壳读盘范围 = 全盘只读（与上游 §1.1 同等暴露面），失效条件见 §11。

### 6.3 CSP 与脚本

- CSP：`default-src 'self'; img-src 'self' mdres: data:; style-src 'self' 'unsafe-inline';
  script-src 'self'; connect-src 'none'`。`connect-src 'none'` 是防外传的关键一条
  （mermaid `securityLevel: 'loose'` 的代价由此兜住）。
- 不注入任何业务脚本；前导脚本只做通道接线。

## 7. 文件 IO 与路径

- 编码：BOM 优先；无 BOM 先 UTF-8 严格解码，失败退 GB18030；保存沿用打开时的编码与 BOM。
- 写入：同目录临时文件 + `File.Replace`/`MoveFileEx`，失败保留原文件；保存后统一尾换行。
- 路径：`\\?\` 长路径、UNC、大小写不敏感比较；比较一律 `OrdinalIgnoreCase`。
- 外部修改：`FileSystemWatcher` + 窗口激活时二次校验（mtime/size），不静默覆盖。
- 关联：`HKCU\Software\Classes\.md`（M4 随安装包）。
- 语言表：`resources/languages.json` 单一来源，C# 侧只做「扩展名 → 语言 id」映射。

## 8. 功能对齐

| 上游能力 | Windows | 里程碑 |
|---|---|---|
| 打开/编辑/保存、脏标记、另存 | 同构 | M1 |
| 多窗口、最近文件、外部修改 | 同构 | M1 |
| 主题（跟系统）、缩放 | 同构 | M1 |
| 右侧文档缩略图（Markdown/代码） | 内核自带，壳给偏好与菜单 | M1（Markdown）/ M3（代码面） |
| 图片显示、粘贴/拖放导入、GC | 同构（`mdres` + 壳落盘） | M2 |
| 文件抽屉、目录模式 | 同构 | M2 |
| 代码只读视图、行号、语言切换 | 同构 | M3 |
| 查找、源码模式 | 同构 | M1 接线 / M3 验收 |
| PDF/HTML/打印 | `PrintToPdfAsync` + 内核导出 | M3 |
| 诊断（自检/截图/日志） | 子命令 | M3 |

## 9. 构建与分发

- `build.ps1`：内核门（`check-kernel.mjs` + `node --test test/`）→ `dotnet publish` →
  组装输出目录（`kernel/dist` 作为资源随包）。
- 依赖安装与内核构建需要网络/子进程：**完全权限下执行一次并缓存**；受限会话只能
  `dotnet build`（需仓库内 `DOTNET_CLI_HOME`/`NUGET_PACKAGES`）。
- 分发（M4）：便携 zip 起步，安装包与签名后置；Runtime 缺失时给下载提示。

## 10. 上游同步

- 快照来源与差异：`kernel/SOURCE.md`；每个里程碑收口对照上游一次。
- 只 cherry-pick 与 Windows 相关的修复；内核行为变更先过 Node 套件再进。
- 上游文档（组件规格、场景）跨仓链接，不复制成「假真相」；Windows 自有组件在实现时另写规格。

## 11. 已知失效条件

- **内核快照漂移**：若上游内核大改（协议版本升级、op 增删），补丁与 C# 客户端需同步升级；
  协议版本不匹配时握手会显式失败，不会静默错乱。
- **`mdres` 全盘只读**：若产品要打开不可信文档，必须收窄到文档目录（与上游 §1.1 同一失效条件）。
- **`--allow-file-access-from-files`**：本方案不使用；若将来为兼容回退到 `file://` 页面，
  需重新评估该开关的暴露面。
- **Win10 1809**：WebView2 支持但未实机验证；M4 前补测。
