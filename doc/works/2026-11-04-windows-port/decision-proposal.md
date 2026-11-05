---
layer: works
module: global
status: active
updated: 2026-11-05
---

# 决策提案：Windows 壳技术选型与内核接入方式

> 状态：**已确认**（2026-11-05）。壳 = WPF + Win11 外观（Mica/圆角/深色标题栏）；
> 内核 = 快照 vendor + 就地补丁；首版范围 = M1 垂直切片。结论已蒸进
> `doc/design/architecture.md`。

## 1. 事实更新（2026-11-05 复核上游）

上游 `md-previewer` 已推进到 `64ba91b`，本次移植的前提全部变化：

| 事项 | 2026-11-04 的认识 | 2026-11-05 的事实 |
|---|---|---|
| 内核 v2 | 规格已评审、零实施，需「复活」 | **已落地并归档**（`2026-11-04-kernel-v2`）：四层内核 + v2 单通道协议，macOS 壳已整体切换 |
| op 数 | 39 | **41**（新增 `setMinimapVisible` / `minimapInfo`，另有 `getState`/`print`/`getContentSize`） |
| 内核结构 | `core/` 单层 | **core 已解体**为七组件 + shared + 8 个端口（`2026-11-04-kernel-core-split`），禁边由 `check-kernel.mjs` 守门 |
| 图片路径语义 | Swift 与 JS 双头，R9 高风险 | **R9 已关闭**：Swift `ImagePathRewriter` 删除，语义单点在 `kernel/semantics`，45 项逐字节对拍 |
| 验证设施 | 仅 macOS selftest | 另有 Node 套件 86 项（契约/语义/core/minimap）+ `FakeShell` + 黄金夹具 |
| 新功能 | 无 | **右侧文档缩略图**（REQ-0011 / SCN-0017 / CMP-minimap，已交付） |
| 组件数 | 7 | 壳 8 + 内核 11（transport/contract/semantics + 七功能 + minimap） |

结论：**「内核要不要 v2」这个问题已经不存在**。Windows 侧不再做任何内核重构，直接消费上游成品。

## 2. 内核接入方式（已收敛，待随壳选型一并确认）

| 方案 | 做法 | 成本 | 风险 |
|---|---|---|---|
| **A. 快照 vendor（推荐）** | 复制上游 `web/`（`src/kernel` + `page.js` + `test/` + `build.mjs` + `check-kernel.mjs`）为 `kernel/`，记录来源 commit；只新增一个 WebView2 宿主前导脚本 | 最低；内核零改动 | 上游后续修复不自动过来，需按主题 cherry-pick |
| B. git submodule | 子模块指向上游仓库 | 零复制 | 上游仓库同时含 macOS 壳与文档，子模块粒度太粗；跨平台构建耦合 |
| C. 独立 npm 包 | 抽包发版 | 最规范 | D5 已否决：两壳独立发版时机未到；且需先改上游发布流程 |

**推荐 A**：上游已经把「Windows 参照物」写进 kernel-v2 的交付物（Node 套件 + FakeShell），
vendor 是它设计时预期的消费方式。同步纪律：来源 commit 记在 `kernel/SOURCE.md`，每次
cherry-pick 一个主题、单独提交。

Windows 侧只需新增一个**宿主前导脚本**（约 20 行，注入在 bundle 求值前）：

```js
window.__mdHostChannel = Object.freeze({
  post: (json) => window.chrome.webview.postMessage(json)
});
window.chrome.webview.addEventListener('message', (e) => window.__mdKernel?.receive(e.data));
```

壳 → 内核用 `PostWebMessageAsString(json)`，内核 → 壳用 `WebMessageReceived` +
`TryGetWebMessageAsString()`；**帧投递不再需要 `ExecuteScriptAsync`**，比 macOS 侧更干净。

## 3. 壳候选方案

| 方案 | 工具链/部署 | 多窗口/自定义边框 | WebView 能力 | 风险 | 结论 |
|---|---|---|---|---|---|
| **WPF + WebView2** | .NET SDK 即可；本机受限会话已实测 `dotnet build` 通过 | `WindowChrome` 成熟，脏确认 async 好写 | 完整（`CoreWebView2` 同一套） | 控件外观传统 | **推荐** |
| WinUI 3 + WebView2 | 另需 Windows App SDK 包（约 200 MB）+ 运行时（本机已装 1.1–1.4）；需完全权限 restore | `AppWindow` + `InputNonClientPointerSource` 样板多；多窗口关闭需显式处理 | 同上 | 构建链与生命周期复杂度 | 备选 |
| Avalonia / Tauri / Electron | 第三方框架 / Rust / 自带 Chromium | — | — | 非原生或体积大，违背 D1 | 否决 |

## 4. WPF 与 WinUI 3 逐项对比

| 维度 | WPF + WebView2 | WinUI 3 + WebView2 | 影响 |
|---|---|---|---|
| 文档渲染 | `Microsoft.Web.WebView2.Wpf` | `Microsoft.UI.Xaml.Controls.WebView2` | 无差异：同一 `CoreWebView2` |
| 构建依赖 | .NET SDK + WebView2 包；VS 自带 1.0.2903 程序集可离线兜底 | + Windows App SDK（约 200 MB）；运行时 1.1–1.4 已装但版本要对齐 | WPF 明显省事 |
| 受限会话构建 | ✅ 已实测 | 需完全权限 restore，未实测 | WPF 风险低 |
| 自定义标题栏 | `WindowChrome`，样板少 | `AppWindow` + `ContentIntoTitleBar` + 非客户区命中 | WPF 省 2–3 天 |
| 多窗口/退出时序 | `ShutdownMode` 直接 | 多窗口关闭不自动退出，`Exit` 显式处理 | WPF 更稳（R-W5） |
| 菜单/树控件 | 原生 `Menu`/`TreeView`，动态项简单 | `MenuBar`/`TreeView` 更现代但样板多 | 视觉 vs 成本 |
| 视觉 | 传统控件；Mica/圆角/深色标题栏可用 DWM 属性加到 WPF 窗口 | 原生 Fluent、动画 | WinUI 优 |
| 体积/启动 | 较小/较快 | 较大/稍慢 | WPF 优 |
| 切换成本 | 壳层（`CMP-app-shell` + XAML）；桥与内核不动 | 同上 | 都可逆 |

**结论**：本项目 90% 可见界面在 WebView 内，壳要解决的是传统桌面问题（窗口、菜单、文件、
抽屉、打印），正是 WPF 强项；WinUI 3 换来标题栏/侧栏观感，代价是构建链、多窗口生命周期与
运行时依赖。Win11 上 WPF 窗口同样能拿 Mica、圆角与深色标题栏，差距进一步缩小。

## 5. 首版范围（M1，已确认）

用户已确认 **M1 垂直切片**：打开 → 编辑 → 保存全链路 + 菜单/多窗口/主题。按上游新事实，
M1 增加两项「几乎免费」的能力，避免后续返工：

- **右侧文档缩略图**：内核自带（默认显示），壳只需偏好 + 菜单勾选 + `init.opts.minimapVisible`
  + `setMinimapVisible` 转发；
- **v2 契约门**：`kernel/` 的 `check-kernel` 与 Node 86 项套件纳入 `build.ps1` 门禁，
  壳侧不必重写这些测试。

## 6. 已确认

1. **壳**：WPF + WebView2，并加 Win11 外观（`DwmSetWindowAttribute` 的 Mica/圆角/深色标题栏，
   Win10 自动降级为普通标题栏）；
2. **内核**：快照 vendor + 就地补丁（`kernel/PATCHES.md` 记录，后续提上游 PR）。

非阻塞默认（按默认执行，收口记录写明）：单实例 + 命名管道转发；文件关联写 HKCU（M4 随安装包）。
