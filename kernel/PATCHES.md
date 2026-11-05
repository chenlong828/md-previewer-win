# 内核平台补丁

本目录的快照对上游的唯一改动都在这里登记。**新增补丁必须先改这里再改代码**；
每个补丁都要有测试，且默认参数下 macOS 行为逐字节不变。

## P-1 PathPolicy：资源 URL 前缀 + 路径风格参数化

- **状态**：已落地（2026-11-05），待提上游 PR。
- **问题**：上游把 macOS 常量写死在语义层：
  - `paths.js#fileUrlFromPath` 只产出 `file://` + posix 分段；
  - `markdown.js#restoreForSave` 只认 `file://` 前缀、默认 `style: 'posix'`；
  - `markdown.js#collectResourceRefs` 同上；
  - `editor/image-view.js` 的 `displayPath`/`resolvePath` 硬编码 `file://` 与 `/` 分隔符。
  Windows 需要另一套 URL 形态（见下），保存时还要把 URL 还原成 `C:\…` 或相对路径；
  不参数化会把宿主 URL 写进 Markdown（数据损坏）。
- **改法**：引入 `PathPolicy = { urlPrefix, style }`，默认 `{ 'file://', posix }`
  （= macOS 现有行为）；由壳在 `init.opts.pathPolicy` 声明，`document` 组件写入
  `state.pathPolicy`。
  - 选「前缀」而非「scheme」：WebView2 的 `CustomSchemeRegistrations` 在 .NET 包装里为
    `null`（1.0.2903.40），自定义 scheme 注册不通；改由壳提供同源路径前缀
    `https://mdpreview.local/__res`，图片与页面同源，CSP 无需放行。
- **文件**：
  - `src/kernel/semantics/paths.js`：新增 `DEFAULT_PATH_POLICY`、`normalizePolicy`、
    `isWindowsAbsolute`、`resourceUrlFromPath`、`resourcePathFromUrl`；旧函数保留（兼容）。
  - `src/kernel/semantics/markdown.js`：`restoreForSave`/`collectResourceRefs` 接受
    `urlPrefix`，改用统一编解码；相对路径输出统一 `/`（markdown 目的地是 URL）。
  - `src/kernel/shared/state.js`：新增 `pathPolicy` 字段（默认 macOS 口径）。
  - `src/kernel/components/document/ops.js`：`init` 解析策略，`getSaveMarkdown` 传入。
  - `src/kernel/components/editor/diagnostics-ops.js`：`getResourceRefs` 传入策略。
  - `src/kernel/components/editor/image-view.js`：`displayPath`/`resolvePath` 走策略。
- **测试**：`test/paths-win32.test.js`（9 项，纯函数）；既有 `semantics.test.js` 50 项零回归。
- **约定**：
  - URL = `<urlPrefix>` + `/` + 逐段编码的绝对路径；盘符冒号编码为 `%3A`（解码端可逆）。
  - 前缀归一：去尾斜杠；若去完只剩 `file:` 这类「scheme 冒号」形态，补回 `//`。
  - UNC 保留 `\\server\share` 双反斜杠（`normalizePath` 会吞掉第二个，故在编解码处特判）。
  - 相对路径一律正斜杠；目录外的绝对路径保留 OS 形式（`C:\…`）。
- **上游合并**：P-1 提案为「PathPolicy 端口」，与 `history/decisions/2026-10-21-editor-kernel-spec.md`
  §4.4 的 PathPolicy 端口一致（上游实现时收成了常量）。合并后删除本补丁并在 `SOURCE.md` 记录。
