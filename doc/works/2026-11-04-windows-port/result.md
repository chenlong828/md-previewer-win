---
layer: works
module: global
status: done
updated: 2026-11-05
---

# 工序结果：M1 垂直切片（Windows 壳跑通「打开 → 编辑 → 保存 → 打包」）

## 1. 结论

M1 达成，且是**可交付形态**：`build.ps1` 一条命令产出 `dist/MDPreview/`（self-contained、
带 `portable.flag` 的绿色目录），实机打开 Markdown 正常渲染（表格/行内代码/公式/缩略图/
暗色主题），保存链路与内核 `getSaveMarkdown` 对齐，单实例转发可用。

M2 的入口已经具备：内核请求应答（`resolveResources`）已实现，图片显示只差 `importImage`
落盘；抽屉与图片粘贴/拖放是下一工序主体。

## 2. 交付物

| 区域 | 文件 | 说明 |
|---|---|---|
| 内核快照 | `kernel/**`（60 文件） | 上游 `64ba91b`；`SOURCE.md` 记来源与同步法 |
| 内核补丁 | `kernel/src/kernel/semantics/{paths,markdown}.js` 等 | P-1 PathPolicy（`urlPrefix` + win32 风格），`PATCHES.md` 登记 |
| 补丁测试 | `kernel/test/paths-win32.test.js` | 9 项；含 macOS 零回归断言 |
| 桥 | `src/MDPreview.Win/Bridge/{KernelClient,KernelHost}.cs`、`webview2-prelude.js` | v2 帧、boot 门控、同源资源、CSP、快捷键转发 |
| 壳 | `src/MDPreview.Win/Shell/DocumentWindow.xaml(.cs)`、`NativeMethods.cs` | 菜单/状态栏/文档生命周期/主题/缩放/外部修改 |
| 基础设施 | `App.xaml.cs`、`AppInfo.cs`、`Io/TextFile.cs`、`Settings/AppSettings.cs`、`Log.cs` | 单实例、路径与绿色版语义、编码/原子写、偏好、日志 |
| 构建 | `build.ps1`、`NuGet.config`、`.gitattributes`、`.gitignore` | 内核门 → 打包 → 壳 → 单测；换行与内核字节保真 |
| 测试 | `tests/MDPreview.Win.Tests/**` | xunit v3（MTP 运行器）16 项 |
| 工具 | `tools/shot.ps1`、`tools/keys.ps1` | 截图与按键注入，验收取证用（非产品代码） |
| 文档 | `doc/design/architecture.md`、`doc/plan/*`、`kernel/PATCHES.md` | 与实现同步后的技术真相 |

## 3. 验收证据（2026-11-05 实机）

```
node kernel/check-kernel.mjs            → check-kernel: OK（43 个内核文件）
paths-win32 9 / semantics 50 / contract 19 / core 11 / minimap 6   → 95 pass, 0 fail
dotnet build src/MDPreview.Win          → 0 warning / 0 error
tests/MDPreview.Win.Tests.exe           → 16 passed, 0 failed
build.ps1 -Dev                          → exit 0（内核门 + bundle + 壳 + 16 项单测）
build.ps1 (Release)                     → dist/MDPreview 133.2 MB，portable.flag 就位
dist/MDPreview/MDPreview.exe 冒烟        → handshake kernel=0.1.0 protocol=2 ops=41
单实例                                  → 第二个进程退出，已有窗口切到新文件
```

界面取证：`artifacts/shot-light.png`、`artifacts/shot-dark.png`（渲染、缩略图、状态栏
字数统计、菜单、暗色壳配色）。两个主题的标题栏颜色随主题切换（DWM CAPTION_COLOR）。

## 4. 过程中定位并修掉的问题

| 现象 | 根因 | 处理 |
|---|---|---|
| `init` 超时（30 s） | 壳在页面加载前投帧，`__mdKernel.receive` 还不存在，帧被丢弃 | `KernelClient.Booted` 门控所有 `SendAsync`；前导脚本暂存早到帧 |
| `Ctrl+S` 无反应 | Chromium 在进程内消费 `Ctrl+S/O/P/F/±/0`，页面收不到 `keydown` | `AreBrowserAcceleratorKeysEnabled = false`，快捷键统一走前导脚本转发 |
| 自定义 scheme 注册 NRE | `CoreWebView2EnvironmentOptions.CustomSchemeRegistrations` 在 .NET 包装里为 `null` | 放弃 scheme，改同源路径前缀 `https://mdpreview.local/__res` |
| CSP 无处可加 | 目录映射后请求不进壳 | 改为壳拦截整站请求并下发 CSP 响应头 |
| GB18030 解码抛异常 | 静态字段初始化早于 `RegisterProvider`；且缺 `System.Text.Encoding.CodePages` 包 | 引用包 + 在解析函数内先注册；54936 失败退 936、再退 UTF-8 |
| VSTest 启动即崩 | `testhost` 监视父进程被拒（`Win32Exception 5`） | 换 xunit v3 + Microsoft.Testing.Platform 运行器 |
| 单测写 `%TEMP%` 被拒 | 受限环境只允许写仓库 | 测试数据落到 `artifacts/test-data/`（启动前设 `MDP_USER_DATA`） |
| `node --test` 失败 | 受限环境 `spawn EPERM` | 逐个直接执行 `test/*.test.js` |
| `build.ps1` 中途退出 | esbuild 进度写 stderr，`$ErrorActionPreference='Stop'` 当错误 | 收窄为 `Continue` + 显式判 `$LASTEXITCODE` |

## 5. 已知偏差与遗留

- `importImage` 未实现：当前若粘贴/拖入图片，壳回 `unknown_op`（M2 补落盘与路径回传）。
- 外部修改提示、关闭前保存确认、最近文件菜单项为**代码就绪但未自动化验证**（依赖人工点击）。
- 暗色主题下缩略图首屏底色偏亮，待 M3 缩略图验收时与上游比对。
- 首次冷启动（新建 WebView2 用户数据目录）约 18 s，二次启动明显更快；M3 可做首启提示。
- `build.ps1` 的 `-SkipGates` / `-SkipTests` 已就位，但 CI 化（GitHub Actions）未做。

## 6. 下一步（M2）

1. `importImage`：`media/image-paste.js` 的载荷 → 壳落盘 `resources/` → 回传相对路径；
2. 文件抽屉：目录树懒加载、宽度拖拽、锚点（复用上游 `drawerView` 语义）；
3. 图片拖放（`AllowDrop` + `DragOver/Drop`，区分 .md 与图片）；
4. 最近文件菜单完善与「清空」；外部修改提示补验收。
