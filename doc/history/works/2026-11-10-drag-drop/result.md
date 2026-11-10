---
layer: works
module: shell
status: active
updated: 2026-11-10
---

# 工序结果：拖入文件打开（W-06，P2）

## 1. 结论

拖入从“只支持图片”扩展为：

- `.md` → 新窗口打开；
- 目录 → 新窗口进入目录模式；
- 图片 → 与原菜单导入同一 `InsertImageFileAsync` 链路（文档未保存时提示先保存）；
- 代码只读窗口忽略图片，不写盘。

实现方式是关闭 WebView2 的 `AllowExternalDrop`，由 WPF `Drop` 直接拿文件路径；页面不再需要
base64 上传（旧的 `dropImage` 宿主消息保留但不再触发）。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/Shell/DocumentWindow.xaml` | 窗口/WebView2 挂 `DragOver`/`Drop`，`AllowExternalDrop=False` |
| `src/MDPreview.Win/Shell/DocumentWindow.xaml.cs` | `OnWindowDragOver`/`OnWindowDrop` 分流；`InsertImageFileAsync` 抽取（菜单/拖入共用）；`ImageExtensions` 表 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 55 passed / 0 failed |
| Markdown 初始化 | `--diag state samples/功能演示.md` | ready |
| 目录模式 | `--diag dir samples` | root/title/entries/drawer 正常 |

## 4. 偏差与遗留

- **真实拖放未在会话内验证**：桌面窗口在屏幕外，无法执行 OS 拖放。若实机发现 WPF 收不到
  Drop，回退 `AllowExternalDrop=true` 恢复页面图片拖放（见 OQ-W14）。
- 拖入图片在未保存文档只提示先保存（与原菜单行为一致）。
- 旧的页面 `dropImage` 前导脚本成为死路径，保留一个版本观察，确认后再删。

## 5. 状态

代码与自动化通过；拖放实机验收见 OQ-W14。
