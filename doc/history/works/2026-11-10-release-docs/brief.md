---
layer: history
module: global
status: frozen
updated: 2026-11-10
---

# 工序 brief：发布与文档收口（W-05，P3）

## 1. 需求

M5 五个工序的收口件：

1. 版本对齐上游 `0.6.0`（csproj `Version/AssemblyVersion/FileVersion`）；
2. README 更新基线、M5 里程碑与主要功能；
3. `doc/README.md` 更新需求数量、计划与历史索引；
4. `doc/design/architecture.md` 回写 M5 真相（命令注册表、右键、目录模式、诊断面）；
5. 同步 REQ-0012/0013 到 Windows 需求卡（平台注记）；
6. roadmap 收口 M5，登记实机验收 OQ-W12–W15。

## 2. 范围

只改文档与 csproj 版本号，不改代码行为。版本号三键同源手改（与上游同一做法，无发布流水线）。

## 3. 验收标准

1. `MDPreview.exe --diag info` 输出 `app 0.6.0.0`；
2. `dotnet build` 0 error、C# 单测 55 passed；
3. README / doc/README / architecture 与实现一致，无「M1–M4」残留作为现状；
4. REQ-0012/0013 有平台注记与 Windows 路径/菜单口径；
5. roadmap M5 五行全部收口，OQ-W12–W15 指向验收项。

## 4. 任务表

| ID | 任务 | 类型 | 组件 | 状态 |
|---|---|---|---|---|
| T-01 | 版本 0.6.0 + README + doc 索引 | doc | — | ✅ 完成 |
| T-02 | architecture 回写 + REQ-0012/0013 同步 | doc | — | ✅ 完成 |
| T-03 | roadmap 收口 + 诊断复核 + 归档 | verify | — | ✅ 完成 |
