---
layer: works
module: global
status: done
updated: 2026-11-05
---

# 工序结果：M1 垂直切片 → M2 抽屉与图片（Windows 壳跑通「打开 → 编辑 → 保存 → 插图 → 打包」）

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

- 外部修改提示、关闭前保存确认为**代码就绪但未自动化验证**（依赖人工点击）。
- 暗色主题下缩略图首屏底色偏亮，待 M3 缩略图验收时与上游比对。
- 首次冷启动（新建 WebView2 用户数据目录）约 18 s，二次启动明显更快；M3 可做首启提示。
- `build.ps1` 的 `-SkipGates` / `-SkipTests` 已就位，但 CI 化（GitHub Actions）未做。

## 6. M2 结果：抽屉 + 图片（2026-11-05）

### 6.1 交付

| 区域 | 文件 | 说明 |
|---|---|---|
| 图片落盘 | `src/MDPreview.Win/Io/ImageStore.cs` | `<文档>/resource/<文档名>/`、`img_NNN`、原名冲突 `-2`、GC、URL/路径编解码 |
| 抽屉取数 | `src/MDPreview.Win/Io/FileTree.cs` | 单层枚举、目录优先、跳过隐藏/`node_modules`、只留可打开文本类型 |
| 抽屉 UI | `DocumentWindow.xaml(.cs)` | 左栏 TreeView + 分隔条 + 上级/用户目录按钮；懒加载展开；双击开新窗口；宽度与开关持久化 |
| 桥 | `KernelClient`/`KernelHost`/`webview2-prelude.js` | `__host:'dropImage'` 宿主消息、`importImage` 请求应答、拖入图片读字节 |
| 菜单 | `DocumentWindow` 编辑/视图 | 「插入图片…」（Ctrl+Shift+I）、「文件抽屉」（Ctrl+\） |
| 测试 | `tests/.../ImageStoreTests.cs`、`FileTreeTests.cs` | 17 项（命名/冲突/校验/GC/编码/枚举/根目录） |

### 6.2 验收证据（实机，`artifacts/mdpreview.log`）

```
selftest paste: imgs=1 https://mdpreview.local/__res/D%3A/.../resource/paste/img_001.png [120x80]
selftest markdown: # 粘贴测试\n\n正文。![](resource/paste/img_001.png)\n
image dropped img_001.png      （拖入路径复用同一落盘/插入链路）
```

- 粘贴与拖入都经「内核识别 → `importImage` → 壳落盘 → `insertImage`」，回传的是同源资源
  URL，图片按真实尺寸渲染（`120x80`）；保存时内核 `restoreForSave` 已还原成相对路径。
- 抽屉截图：根目录名、↑/⌂、`resource/`、`env.txt`、`paste.md`（当前文档加粗选中）。
- 单元测试 34 项全绿（TextFile 8 / AppSettings 8 / ImageStore 9 / FileTree 5 + 4 theory）。

### 6.3 取舍与偏差

- 拖入 **.md 文件**不打开新窗口：Chromium 不把文件真实路径交给页面，反查路径需
  关掉 `AllowExternalDrop` 走原生 OLE，本轮不做（上游用原生拖放实现）。当前只接管图片。
- 抽屉不展示二进制/非文本文件，也不做「锚点跳转」（上游的 anchor 菜单）——M3 与
  代码视图一起补。
- 抽屉不自动 rescan（切回窗口不重建树），靠重新打开或切根刷新；上游是激活即 rescan。

## 7. 下一步（M3）

1. 代码只读视图（44 语言 / 行号 / 缩略图）与语言表单一来源 `resources/languages.json`；
2. 右侧缩略图验收（暗色首屏底色）与折叠交互；
3. 查找栏与源码模式验收；PDF/HTML/打印（`PrintToPdfAsync` + 内核导出）；
4. 诊断子命令（自检/截图/日志），并可复用本轮临时探针的「合成粘贴/拖放」思路。
