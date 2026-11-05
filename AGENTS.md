# AGENTS.md — MDPreview for Windows

本文件是常驻约定；开发流程的完整规则在 `.agents/skills/` 的五个 skill 里，冲突时以 skill 为准。

## 项目是什么

md-previewer（macOS，Swift/AppKit + WKWebView）的 Windows 移植版。目标是**功能对齐、交互同构**：

- 壳：WPF（.NET 9）+ WebView2，原生负责窗口/菜单/文件 IO/编码/监听/导出/抽屉/诊断。
- 内核：上游 `web/` 的**快照 vendor**（`kernel/`），Milkdown 7.7 / ProseMirror，含 v2 四层结构。
- 桥：**v2 JSON 消息协议**（41 op / 7 通知 / 2 内核请求 / 5 错误码），见
  `kernel/src/kernel/contract/ops.js` 与 `doc/design/architecture.md` §5。

## 必读

1. `doc/design/architecture.md`：现行技术真相（栈、桥、资源加载、文件 IO、补丁、安全取舍）。
2. `doc/plan/roadmap.md`：里程碑；`doc/plan/open-questions.md`：待决项。
3. `doc/works/2026-11-04-windows-port/brief.md`：当前工序任务表。
4. `.agents/skills/`：五个开发 skill（req-scenario-spec / design-before-coding / arch-design-spec / doc-layout-spec / dev-execution-spec）。

## 硬约束

- **不修改上游仓库** `D:\Github\md-previewer`；它只读。
- **内核是快照**：只允许 `kernel/PATCHES.md` 记录在案的 Windows 补丁；任何其它内核改动先改上游再同步。
- **壳不解析、不改写 Markdown 文本**：路径改写、保存还原、引用收集一律在内核 `semantics/`；
  禁止「壳里把 mdres 换回路径」这类兜底（会复活 R9）。
- **内核零宿主感知**：除 `kernel/src/kernel/transport/` 外不得出现 `chrome.webview` 等宿主符号；
  Windows 通道接线只放在注入的前导脚本里。
- **磁盘唯一写入口是壳**：内核只提交意图（`importImage` 请求、`getResourceRefs`）。
- **契约里不出现框架类型**：桥 payload 只允许 JSON 原语/数组/对象。
- **语言表单一来源**：`resources/languages.json`；C# 与内核都由它派生，禁止手抄清单。

## 变更纪律

- 按 `arch-design-spec` 定级：新增组件/改契约/改数据所有权/改内核语义 = P1，需人工确认后再编码。
- 一次任务只动一个组件；提交前 `git status` + `git diff` 过目；一个 commit 一件事。
- 文档按五态落位：需求 `doc/requirements/`、调度 `doc/plan/`、真相 `doc/design/`、
  执行 `doc/works/<工序>/`、记忆 `doc/history/`。frontmatter 必写，路径与 layer/status 一致。

## 构建与运行环境（本机实测）

| 事项 | 现状 | 做法 |
|---|---|---|
| .NET SDK | 8.0.427 / 9.0.320 | 目标框架 `net9.0-windows` |
| WebView2 Runtime | 156.0.4314.58 已装 | 壳启动检测，缺失提示安装 |
| WebView2 SDK | NuGet `Microsoft.Web.WebView2`；VS 本地另有 1.0.2903.40 可离线兜底 | PackageReference；restore 需完全权限 |
| Node | 仅 DSH 提供的 `node`（Electron `ELECTRON_RUN_AS_NODE`）；**无 npm** | 内核构建用 `pnpm`，需完全权限 |
| 网络 | 受限沙箱禁止出网；完全权限下 nuget.org / npmmirror 可达 | 依赖安装与内核构建在完全权限下做一次并缓存 |
| 子进程管道 | 受限沙箱禁止（`spawn EPERM`） | esbuild / pnpm 不能在受限模式运行 |
| dotnet build | 受限模式可运行 | 设 `DOTNET_CLI_HOME`、`NUGET_PACKAGES` 到仓库内（见 `build.ps1`） |

## 常用命令

```powershell
pwsh ./build.ps1                     # 全量：内核门 + 壳 + 组装
pwsh ./build.ps1 -SkipKernel         # 只编译 C# 壳
dotnet build src/MDPreview.Win -c Debug
dotnet test tests/MDPreview.Win.Tests
node kernel/check-kernel.mjs          # 内核禁边
node --test kernel/test/              # 内核契约/语义/core 套件
```

## 与用户的沟通

- 结构性选择（壳选型、仓库形态、契约变更、内核补丁方式）先给 2–3 个选项与推荐，一次问清。
- 汇报用中文；先说结论，再说证据（命令输出、文件路径）。
