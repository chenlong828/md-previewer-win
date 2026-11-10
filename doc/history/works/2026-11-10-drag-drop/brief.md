---
layer: history
module: shell
status: frozen
updated: 2026-11-10
---

# 工序 brief：拖入文件打开（W-06，P2）

## 1. 需求

拖入文件不再只支持图片：

1. 拖入 `.md` → 新窗口打开（一文档一窗口）；
2. 拖入目录 → 新窗口进入目录模式；
3. 拖入图片 → 与原粘贴/菜单导入同一落盘/插入链路（需要文档已保存）；
4. 拖到代码只读窗口的图片忽略，不写盘。

## 2. 设计

- 关掉 WebView2 的 `AllowExternalDrop`（WPF 控件属性），让路径由 WPF `Drop` 直接拿到，
  不再经页面上传 base64（旧路径拿不到文件名/路径）；
- 窗口与 WebView2 都挂 `DragOver`/`Drop`，`e.Handled` 防止冒泡重复处理；
- 图片复用 `InsertImageFileAsync`（菜单与拖入共用）；`.md`/目录走 `App.OpenDocument`；
- 删除“只支持图片”的限制，但不改图片落盘/GC 语义。

## 3. 非目标

- 不做拖到窗口内的光标位置插入（图片仍插入当前光标处）；
- 不做多文件批量导入的进度 UI；多个 `.md` 各开一个窗口；
- 不做 OLE 拖出（应用 → 资源管理器）。

## 4. 验收标准

1. `dotnet build` 0 error、C# 单测 55 passed、`--diag state`/`--diag dir` 正常；
2. 实机：拖 `.md` 到窗口开新窗口；拖目录进目录模式；拖图片到已保存文档落盘并显示；
3. 实机：拖图片到代码只读窗口不写盘；
4. 若实机发现拖放完全不触发，回退为 `AllowExternalDrop=true` 并恢复页面图片拖放，登记结论。

## 5. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | WPF Drop 接线 + 图片/文档/目录分流 | change | CMP-app-shell | ✅ 完成 |
| T-02 | 构建/单测/诊断 + result | verify | — | ✅ 自动化通过；实机拖放见 OQ-W14 |

## 6. 装载清单

1. `src/MDPreview.Win/Shell/DocumentWindow.xaml(.cs)`（现有拖放与图片导入）；
2. `src/MDPreview.Win/Bridge/webview2-prelude.js`（旧图片拖放路径）；
3. `src/MDPreview.Win/Io/ImageStore.cs`（`ImportFile`/`ToResourceUrl`）。
