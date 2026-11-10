---
layer: works
module: global
status: active
updated: 2026-11-10
---

# 工序结果：发布与文档收口（W-05，P3）

## 1. 结论

M5 收口完成：

- 版本对齐上游：`app 0.6.0.0`（csproj `Version/AssemblyVersion/FileVersion` 三键同源）；
- README 更新到 `05493a3` 基线，补 M5 里程碑与「主要功能」；
- `doc/README.md` 需求 13 张、计划 M0–M5、历史索引更新；
- `architecture.md` 回写 M5 真相：命令注册表、右键追加/坐标/表格门控、行号广播、打印、
  目录模式与锚点，新增 §8.1；
- REQ-0012/0013 同步为 Windows 版（绝对 Windows 路径、资源管理器、平台注记）；
- roadmap M5 五行收口，实机与拖入待办指向 OQ-W12–W15。

## 2. 落点

| 文件 | 改动 |
|---|---|
| `src/MDPreview.Win/MDPreview.Win.csproj` | 版本 0.6.0 |
| `README.md` | 基线、M5、主要功能、文档索引 |
| `doc/README.md` | 13 张需求 / M5 / 历史索引 |
| `doc/design/architecture.md` | 组件地图、§8 功能对齐、§8.1 菜单/右键/目录、§10/§11 |
| `doc/requirements/REQ-0012-copy-file-path.md`（新） | 复制路径（Windows 口径） |
| `doc/requirements/REQ-0013-reveal-in-finder.md`（新） | 资源管理器定位（Windows 口径） |
| `doc/plan/roadmap.md` | M5 收口与待办指针 |

## 3. 验证

| 门 | 命令 | 结果 |
|---|---|---|
| 版本 | `MDPreview.exe --diag info` | `app 0.6.0.0` |
| 壳编译 | `dotnet build src/MDPreview.Win -c Debug` | 0 error |
| C# 单测 | `tests/MDPreview.Win.Tests.exe` | 55 passed / 0 failed |
| 目录诊断 | `--diag dir samples` | root/title/entries/drawer 正常 |

## 4. 遗留

- 实机交互验收：OQ-W12（右键）、OQ-W13（菜单/打印/剪贴板/链接）、OQ-W15（目录模式/锚点）；
- 拖入 `.md`：OQ-W14；
- 打包产物与 Win10 实机：OQ-W10，发布前在正常桌面跑一次 `build.ps1` 即可。

## 5. 状态

M5 工序收口并归档；后续实机验收按 open-questions 逐项关闭。
