---
layer: plan
module: global
status: current
updated: 2026-11-05
---

# 风险登记（Windows 版）

| ID | 风险 | 概率/影响 | 缓解 | 触发信号 |
|---|---|---|---|---|
| R-W1 | **内核快照漂移 + PathPolicy 补丁**：上游改路径语义或协议升级，补丁失效 | 中/高 | 补丁只碰 `semantics/paths.js` 与会话选项，用测试锁住；`kernel/SOURCE.md` 记差异；协议版本不匹配时握手显式失败 | 上游改 `paths.js`/`manifest`；Node 套件在 vendor 后失败 |
| R-W2 | 受限沙箱无法装依赖/跑 esbuild（网络与子进程管道被禁） | 高/中 | 依赖安装与内核构建在完全权限下一次完成并缓存 `kernel/node_modules`；`build.ps1` 支持 `-SkipKernel` | `spawn EPERM`、NuGet 还原失败 |
| R-W3 | `mdres` 授予全盘只读，恶意文档可读本机文件（不可外传由 CSP 兜底） | 低/中 | 与上游 §1.1 同口径；文档化失效条件；后续可收窄到文档目录 | 产品开始打开外部不可信文档 |
| R-W4 | 编码误判（GB18030 vs UTF-8）导致保存变形 | 中/高 | BOM 优先；UTF-8 严格解码失败才退 GB18030；往返测试覆盖中文样例 | 用户报告乱码、diff 全量变化 |
| R-W5 | 多窗口 WebView2 生命周期（内存、进程崩溃、退出卡住） | 中/中 | 每窗口独立 `CoreWebView2`；关闭即释放；崩溃事件上报并提示重开 | 退出残留进程、第二窗口白屏 |
| R-W6 | 输入法组合态与 WebView2 焦点切换（中文输入丢字/重复） | 中/中 | 组合期不拦截按键；`AcceleratorKeyPressed` 仅在非组合态处理快捷键 | IME 组合时触发菜单命令 |
| R-W7 | PDF/打印版式与上游差异（分页、边距、字体） | 中/低 | 内核导出 HTML 为唯一内容源；`PrintToPdfAsync` 参数固定并留人工点验 | 用户对比 PDF 发现分页差异 |
| R-W8 | 上游需求/文档继续演进，Windows 文档落后 | 高/低 | 每里程碑对照一次；需求卡整卡同步并做平台化改写 | 上游新增 REQ/SCN |
| R-W9 | 缩略图 DOM 快照在低端机/长文上卡顿（内核默认开启） | 中/中 | 沿用内核 250ms 防抖与 rAF；壳侧可先关默认值再定位 | 滚动掉帧、快照耗时 > 50ms |

## 已关闭

- **R9 路径语义漂移**（上游 2026-11-04 关闭）：Swift 改写器已删除，语义单点在
  `kernel/semantics`；Windows 继续守「壳不改写 Markdown」。
