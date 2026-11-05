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

## 7. M3 结果：代码视图 + 导出 + 诊断（2026-11-05）

### 7.1 交付

| 区域 | 文件 | 说明 |
|---|---|---|
| 语言表 | `resources/languages.json`、`Io/CodeLanguages.cs` | 44 门语言/78 扩展名，单一来源；壳只做扩展名 → id |
| 语言表门 | `tools/check-languages.mjs`、`build.ps1` | 每个 id 必须能被内核 refractor 高亮，扩展名/展示名不重复 |
| 代码文档 | `DocumentWindow.LoadCodeAsync` | 非 Markdown 扩展名走 `setCode`，等 `ready` 再送（早送会被内核拒），只读、不写盘、不产生脏标记 |
| 大文件降级 | 同上 | UTF-8 字节 > 2 MB → `plaintext`，状态栏与日志标注 |
| 导出 | `ExportPdfAsync` / `ExportHtmlAsync` | PDF 走 `PrintToPdfAsync`（背景开、页边距 0.4"、无页眉页脚）；HTML 走内核 `exportStandaloneHTML` |
| 诊断 | `--diag info/code/state/export-html/export-pdf` | 无界面自检，stdout + 退出码，远程/锁屏也能验收 |
| 菜单 | 文件 → 导出 PDF/HTML；视图 → 代码语言 | 语言项按当前扩展名打勾 |

### 7.2 验收证据（`--diag`，无界面）

```
--diag info        languages 44   web=...\web (kernel=True)
--diag code a.cs   {"mode":true,"language":"csharp","allowHighlight":true,"textLength":7558,
                    "lineCount":208,"tokenCount":1147,"lineNumbers":true}
                   minimap={"surface":"code","visible":true,"mode":"fit","scale":0.1395,"snapshotCount":1}
--diag state a.md  state={"ready":true,"sourceMode":false,"codeMode":false,"theme":"dark",...}
                   source on {"sourceMode":true} / source off {"sourceMode":false}
                   find open {"ok":true} / find after {"active":true,"query":"","count":0} / close ok
--diag export-html html bytes=45839，正文 <h1 id="">拖入测试</h1><p>正文。</p>
--diag export-pdf  pdf ok=True，17 579 字节，%PDF-1.4，1 页
```

- 代码视图真实渲染了 208 行 / 1147 个高亮 token，行号开；代码面缩略图 `surface:code` 生效。
- 源码模式双向切换、查找栏开/关都拿到内核回执；查找匹配数由内核套件覆盖（`find` 19 项）。
- 单元测试 43 项全绿（新增 CodeLanguages 5 + 3 theory）。

### 7.3 取舍

- 「打印」走系统打印对话框（内核 `print` → `window.print()`）未单独做，PDF 导出覆盖了
  主要用途；如需要再补菜单项。
- 导出的 HTML 里图片 `src` 仍是内核当前的资源 URL 形态，跨机器分发前应先保存一次
  （`getSaveMarkdown` 会把图片还原成相对路径）——与上游行为一致，未额外改写。

## 8. M4 结果：绿色分发（2026-11-05）

### 8.1 交付

| 区域 | 文件 | 说明 |
|---|---|---|
| 便携包 | `build.ps1` | `dist/MDPreview/` + `dist/MDPreview-<ver>-win-x64-portable.zip`（bsdtar 打包，条目 `/` 分隔） |
| 打包自检 | `build.ps1` | zip 解压到 `dist/.smoke` 跑 `--diag info`，要求 `kernel=True`、`languages 44`、`portable.flag` |
| 文件关联 | `Io/FileAssociation.cs`、`tools/associate.ps1` | HKCU 写/撤销，安装前备份原默认值、卸载还原；5 个扩展名 |
| 图标 | `tools/make-icon.ps1`、`resources/app.ico` | 16/24/32/48/64/128/256 七尺寸；csproj `ApplicationIcon` 进 exe |

### 8.2 验收证据

```
build.ps1 -SkipKernel
  压缩包：dist\MDPreview-1.0.0-win-x64-portable.zip（57.7 MB）
  web        dist\.smoke\web (kernel=True)
  languages  44
  ==> 完成
zip entries=318  → portable.flag / web/index.html / resources/languages.json 均为 '  ' 分隔
exe icon: 32x32（ExtractAssociatedIcon 成功）

tools\associate.ps1 -Install → .md: Markdown -> MDPreview.Markdown（其余 4 个 (无) -> ...）
                            status: backup=Markdown（.md）
tools\associate.ps1 -Uninstall → .md -> Markdown，其余恢复「无默认值」，progId 注销
```

- 关联幂等：重复 `-Install` 不会把自身 ProgId 备份成「原值」（本轮实测踩到并修掉）。
- 关联还原：撤销后 `.md` 回到原来的 `Markdown` ProgId，其余扩展名默认值删除。

### 8.3 偏差与遗留
- **Win10 1809 实机验证未做**：本机是 Win11 24H2。已做的静态保障：DWM 圆角/标题栏色
  调用失败即忽略（Win10 保留默认标题栏）；打包用 `tar.exe`（1803+ 自带）；WebView2 Runtime
  缺失时启动即提示。真机验证需要在 Win10 上解压 zip 跑一遍（步骤见 README）。
- **沙箱内壳进程写 `HKCU\Software\Classes` 被拒**（`reg.exe` 同样被拒，终端里的 PowerShell
  可以写）：这是执行环境给的受限令牌，不是代码问题；因此提供 `tools\associate.ps1` 作为
  等效入口，并在壳内把该异常转成「请改用脚本」的提示。
- 未做代码签名：SmartScreen 会提示「未知发布者」，绿色版裸 zip 分发的固有限制。

### 8.4 试用反馈修复（2026-11-05，用户实机）

- **反馈**：「使用正常，没有左侧可以拉出来的抽屉」——抽屉默认 `false` 且只有「视图 → 文件抽屉」
  一个入口，等于藏起来了。
- **修复**：默认改为打开（`AppSettings.DrawerVisible = true`），并在状态栏左下加常驻
  「☰ 文件」按钮（打开时给浅底、悬停高亮，tooltip 随状态变），与视图菜单、Ctrl+\ 三处同源；
  单元测试覆盖默认值。实机截图确认 drawer 与按钮都在（`artifacts/shot-dist-drawer.png`）。
- **顺带修掉 1 个会咬人的问题**：`build.ps1` 原来 `Remove-Item dist/MDPreview` 会把绿色版
  `data/`（设置、最近文件、WebView2 配置）一起删掉。现在发布前暂存 `dist/.data-stash`、
  发布后还原，打包时 `--exclude data`，重建不再吞用户数据（用 `data/sentinel.txt` 实测保留）。
- **遗留**：窗口较窄（默认 1080）且文档含宽表格时，markdown 区会出现横向滚动条
  （缩略图预留 146px 后可用宽度不够）。不影响编辑，暂不改布局；需要的话可以把默认窗口
  调宽或给表格加可换行样式。
