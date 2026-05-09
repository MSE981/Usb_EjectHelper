---
name: usb-eject-helper
description: >-
  Guides development of the UsbEjectHelper .NET 8 WinForms project: architecture,
  safe defaults, P/Invoke layering, testing, UI concurrency, and documentation sync.
  Use when editing UsbEjectHelper/, adding eject/scan/process/force-eject features,
  or preparing PRs for this repo.
disable-model-invocation: false
---

# Skill: USB Eject Helper 开发约定

> **存放说明**：本文件在 `doc/` 便于版本化与评审。若需在 Cursor 中作为「项目 Skill」自动加载，可将本文件复制到 `.cursor/skills/usb-eject-helper/SKILL.md`（或链式引用本文档）。

## 何时使用本 Skill

- 修改或扩展 `UsbEjectHelper/` 下的 C# 代码、测试、WinForms UI。
- 涉及 **安全弹出、占用扫描、结束进程、强制卷卸载、审计日志、设置项**。
- 用户说「按 USB 弹出工具那套流程做」或引用 `phase2-development-plan`。

## 项目概要

- **运行时**：.NET 8，`net8.0-windows`，WinForms，托盘常驻。
- **组合根**：`ServiceComposer` 构造所有服务；新增服务必须在此注册并向 UI 注入接口。
- **分层**：
  - `Native/NativeMethods.*.cs` — P/Invoke，`partial class NativeMethods`。
  - `Core/` — 业务逻辑、`ScanSummary` / `HandleScanResult`、终止与强制弹出服务。
  - `Core/Abstractions/` — `IHandleScanner`、`IProcessTerminator`、`IForceEjectService`、`IActionAuditLog` 等。
  - `Settings/` — `AppSettings` JSON；`StartupManager` HKCU Run.
  - `UI/` — `MainWindow`、`SettingsForm`、各类对话框。

## 必须遵守的安全约定

1. **默认关闭危险能力**：深度扫描、结束进程、强制结束、`ForceEject` 等独立布尔开关，默认 `false`（审计日志可默认开，见 `AppSettings`）。
2. **首次开启二次确认**：与 `SettingsForm` 中 `EnableDeepHandleScan` 相同模式；文案需区分「结束进程」与「强制弹出」。
3. **强制弹出**：仅允许对 **`DriveType.Removable`** 走 dismount/eject；系统盘/固定盘在 Validate 阶段拒绝。
4. **进程风险**：`ProcessRiskTier` — `Critical` 禁止任何关闭；`High` 强制结束需用户**打字精确匹配**进程名；`Normal` 用勾选 + 确认。
5. **主流程顺序**：弹出失败 → 三选一（扫描 / 关闭进程 / 强制弹出）；**关闭进程后不自动重试弹出**，由用户处理保存对话框后再点弹出。
6. **审计**：`IActionAuditLog.Append`；`consent` 字段记录同意方式；失败写入不得抛到 UI。

## UI 并发

- 弹出/扫描等用 `Interlocked.CompareExchange` 做 `_busy` 互斥。
- 从异步/对话框回到主流程时，若可能嵌套锁，用 `BeginInvoke` 延迟触发下一动作。
- 长耗时：`Task.Run` + `CancellationToken`；禁用相关按钮期间防连点。

## 测试策略

- **路径**：`UsbEjectHelper/tests/UsbEjectHelper.Tests/`。
- **句柄/扫描相关**：保持 `xunit.runner.json` **串行**，避免并行耗尽线程池或句柄竞争。
- **集成**：可 `Process.Start("cmd.exe", ...)` 启子进程再 `Kill` / `WM_CLOSE`，测完必须释放。
- **Moq**：mock `IProcessInspector` 等测 `ProcessTerminator` 拒绝路径。
- **设置**：`AppSettings.OverrideFilePath` 使用临时 JSON，测试类 `IDisposable` 清理。

## 实现顺序（简版）

与 [`workflow-usb-eject-helper.md`](workflow-usb-eject-helper.md) 一致：**文档（若需）→ Native → 模型/接口 → 服务 → AppSettings + Composer → 测试 → UI → manual-test-plan + README → build/test/smoke → commit**。

## 文档同步义务

| 变更类型 | 更新 |
|---|-----|
| 用户可见行为 / 新设置 | `README.md`（功能、配置表、测试说明） |
| 需真机验证 | `doc/manual-test-plan.md` 新条目（如 §K） |
| 大功能阶段 | `doc/plan.md` 或 `doc/phase*-development-plan.md` |

## 提交信息风格

- 前缀：`feat(usb-eject):`、`fix(usb-eject):`、`test(usb-eject):`、`chore(usb-eject):`
- PR 编号可在主题中标注：`PR10 …`
- 长说明用 `git commit -F path`（PowerShell 避免 heredoc 问题）

## 关键文件速查

```
UsbEjectHelper/src/UsbEjectHelper/
  App/ServiceComposer.cs
  Core/HandleScanner.cs
  Core/EjectService.cs
  Core/ProcessInspector.cs
  Core/ProcessTerminator.cs
  Core/ProcessTerminator.RestartManager.cs
  Core/ForceEjectService.cs
  Core/ActionAuditLog.cs
  Settings/AppSettings.cs
  UI/MainWindow.cs
  UI/SettingsForm.cs
  UI/EjectFailureDialog.cs
  UI/CloseProcessesDialog.cs
  UI/ConfirmTerminateDialog.cs
  UI/ForceEjectConfirmDialog.cs
```

## 与 C# 参考书 Skill 的关系

- 语言层 API、nullable、`Span`、async 模式可结合用户已配置的 **C# 12 in a Nutshell** skill。
- **本 Skill** 负责**本项目**的 Windows 行为、安全闸门与仓库内文件约定；二者同时生效时，以本 Skill 的权限与产品安全约束为准。
