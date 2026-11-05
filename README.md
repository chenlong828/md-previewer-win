# MDPreview for Windows

**Windows 上的 Typora 风格 Markdown 编辑器。** 打开即写、所见即所得、不需要预览分栏；
也可以把代码文件当作只读文档阅读。是 [md-previewer](https://github.com/chenlong828/md-previewer)
（macOS，Swift + WKWebView）的 Windows 移植版：**同一套 JS 内核，换一个原生壳**。

![Windows 10 1809+](https://img.shields.io/badge/Windows-10%201809%2B-black)
![WPF + WebView2](https://img.shields.io/badge/WPF-WebView2-blue)
![License MIT](https://img.shields.io/badge/license-MIT-green)

## 现状

上游 `md-previewer@64ba91b` 已于 2026-11-04 完成内核 v2 重构与组件拆分，并交付右侧文档
缩略图。Windows 侧因此不再重建内核，直接以快照方式接入，只补一处平台接缝（路径/URL 策略）。

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M0 方向确认 | 壳选型、内核接入方式、首版范围 | ✅ WPF 壳 + 内核快照 |
| M1 垂直切片 | 内核 vendor + 桥 + WebView2 宿主 + 文档 IO + 壳骨架 + 打包 | ✅ 2026-11-05 |
| M2 浏览与媒体 | 文件抽屉、图片粘贴/拖放、导入 GC | ✅ 2026-11-05 |
| M3 阅读与输出 | 代码视图（44 语言）、导出 PDF/HTML、诊断子命令 | ✅ 2026-11-05 |
| M4 分发 | 便携 zip、文件关联、图标 | ⏳ |

## 架构

```text
WPF 壳（net9.0-windows）                     WebView2
窗口 · 菜单 · 文件 IO · 编码 · 抽屉 · 打印      https://mdpreview.local（内核产物 + 本地图片）
        │                                          │
        └──── v2 JSON 帧（41 op / 7 通知 / 2 请求）──┘
                    内核：transport · contract · semantics
                          components{document,editor,code,find,output,media,minimap}
```

- 内核产物来自上游 `web/`（Milkdown 7.7 / ProseMirror），由壳按同源 URL 读盘供给，不联网。
- 本地图片走同源前缀 `https://mdpreview.local/__res/<编码后的绝对路径>`（全盘只读；
  CSP `connect-src 'none'` 断外传）。
- 磁盘写入只在壳：编码（UTF-8/GB18030）、原子写、尾换行归一。

## 快速开始

要求：Windows 10 1809+，[.NET 9 SDK](https://dotnet.microsoft.com/download)，
[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win11 自带），
Node.js 18+ 与 pnpm（构建内核）。

```powershell
pwsh ./build.ps1                 # 内核门 + 内核打包 + 壳 Release + 单测 → dist/MDPreview/
pwsh ./build.ps1 -Dev            # 只做开发构建（Debug，不组装绿色目录）
pwsh ./build.ps1 -SkipKernel     # 内核产物已存在时只编壳
dist/MDPreview/MDPreview.exe samples/功能演示.md
```

`dist/MDPreview/` 是**绿色目录**：整体拷贝即可运行，`portable.flag` 存在时用户数据
（设置/日志/WebView2 配置）写在 `exe/data/` 下，不污染 `%APPDATA%`。

## 文档

- **架构真相** → [doc/design/architecture.md](doc/design/architecture.md)
- **里程碑与风险** → [doc/plan/roadmap.md](doc/plan/roadmap.md) ·
  [risks.md](doc/plan/risks.md) · [open-questions.md](doc/plan/open-questions.md)
- **需求** → [doc/requirements/](doc/requirements/)（11 张，同步自上游）
- **当前工序** → [doc/works/2026-11-04-windows-port/brief.md](doc/works/2026-11-04-windows-port/brief.md)
- **选型提案** → [decision-proposal.md](doc/works/2026-11-04-windows-port/decision-proposal.md)
- **导航与开发规范** → [doc/README.md](doc/README.md) · [AGENTS.md](AGENTS.md) ·
  [.agents/skills/](.agents/skills/)

## 与上游的关系

- 只读上游：`kernel/` 是 `md-previewer@64ba91b` 的 `web/` 快照，差异记在 `kernel/SOURCE.md`。
- 文档：技能与需求卡同步；场景/组件规格在 Windows 侧实现时另写，未实现前只链上游，避免「假真相」。
- 许可：MIT，见 [LICENSE](LICENSE)。
