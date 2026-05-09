# Phase 2 Development Plan — 弹出失败的主流程改造与有梯度的进程关闭

> 状态：**计划阶段**（未开工）
> 日期：2026-05-09
> 上一阶段：MVP 完成（PR1 ~ PR9，89 个 xunit 测试通过）
> 主计划：见 [`plan.md`](plan.md) 阶段 2

---

## 1. 目标 & 不做

### 1.1 解决的问题

阶段 1 已经能：识别可移动盘 → 一键弹出 → 失败时扫描占用。
但**扫描出占用进程后，用户没有任何在程序内的恢复手段**，必须手动去任务管理器 / 资源管理器关掉应用，再回到本程序重试弹出。这条路径太碎。

阶段 2 把"弹出失败 → 恢复手段 → 重试弹出"做成程序内的**主流程**，让用户不离开本工具就能完成全部恢复动作。

### 1.2 范围（要做）

1. **弹出失败后的三选一对话框**（核心 UX 改造）
   - 扫描占用进程（现有）
   - 安全关闭占用进程后由**用户**重试（新）
   - 🔴 强制弹出（新；红色 + 2s 延时确认）
2. **安全关闭进程**：四层方法模型（L1 揭示 / L2 WM_CLOSE / L3 Restart Manager / L4 TerminateProcess）
3. **强制弹出**：卷级 dismount + 强制 eject（FSCTL + IOCTL 组合）
4. **关键进程保护**：扩展现有 13 项 Critical 名单为三级 Tier
5. **审计日志**：所有关闭 / 强制弹出动作写 JSON Lines
6. **设置开关**：默认全部安全，每个高风险动作独立闸门
7. **测试**：自动化集成测试 + 手动测试清单 K-1..K-7

### 1.3 明确不做（推到阶段 3 或更后）

- ❌ 不做"一键关闭所有占用"自动化（永远要用户勾选）
- ❌ 不做跨用户 / 高完整性进程关闭（管理员模式留给阶段 3）
- ❌ 不杀关键进程，即使用户在管理员模式下，名单内的就是不可杀
- ❌ 不在程序内集成"以管理员身份重启自己"（阶段 3 再做 UAC 提权）
- ❌ 不做卷级写缓存的主动刷新（FlushFileBuffers 跨进程要求 SeBackupPrivilege，阶段 3 再说）

---

## 2. 主流程（弹出失败 → 三选一）

### 2.1 状态机

```text
┌─────────────────┐
│  [弹出] 按钮     │
└────────┬────────┘
         ▼
   CM_Request_Device_EjectW
         │
   ┌─────┴─────┐
   │ 成功      │ 失败/否决
   ▼           ▼
  done    ┌──────────────────────────────────────┐
          │ EjectFailureDialog (新)              │
          │  ① 扫描占用进程                      │
          │  ② 安全关闭占用进程（不自动重试弹出）│
          │  🔴 强制弹出（2s 延时 + 风险提示）   │
          │  [关闭]                              │
          └────────────┬─────────────────────────┘
                       │
        ┌──────────────┼─────────────────────────┐
        ▼              ▼                         ▼
    OnScan         CloseProcessesDialog     ForceEjectConfirmDialog
    (现有)              │                         │
                        ▼                         ▼
                 选中要关的 PID + 方式      显示风险条款 + 2s 倒计时
                  L2 / L4 逐个执行                │
                        │                         ▼
                        ▼                  FSCTL_LOCK_VOLUME →
                 完成后回主窗口            FSCTL_DISMOUNT_VOLUME →
                 提示"X 个已退出，请处理   IOCTL_STORAGE_EJECT_MEDIA
                  保存对话框后重试弹出"           │
                        │                         ▼
                        │                       成功
                        │                         ↓
                        ◀─── 用户手动再点弹出 ────┘
                        │
                       failed loop
                        │
                        ▼
              EjectFailureDialog 再次出现
```

### 2.2 为什么"安全关闭后不自动重试弹出"

应用收到 WM_CLOSE 后通常会**弹"是否保存"对话框**。这时候有三种结果：

| 用户在保存对话框点 | 应用 |
|---|---|
| 保存 | 写完磁盘 → 进程退出 |
| 不保存 | 立即退出 |
| 取消 | **进程不退**，仍持有句柄 |

如果我们立即重试弹出：
- 取消那条会让我们的"重试"必败
- 即使保存那条，写盘还没完，重试会让句柄检查通过但写缓冲未刷，可能丢数据

所以策略是：**关闭进程后回主窗口、自动重新扫描占用、提示用户处理保存对话框、由用户决定何时再点弹出**。这把决策权留给用户。

### 2.3 强制弹出的实现路径

```text
1. CreateFile(\\.\E:, GENERIC_READ|WRITE, FILE_SHARE_READ|WRITE|DELETE, OPEN_EXISTING)
2. DeviceIoControl(FSCTL_LOCK_VOLUME)         # 失败也继续
3. DeviceIoControl(FSCTL_DISMOUNT_VOLUME)     # 必须成功；失败则报错退出
4. CloseHandle(volume)
5. CM_Request_Device_EjectW                    # 此时句柄已无，eject 必成功
   或 IOCTL_STORAGE_EJECT_MEDIA               # 备选
```

**关键细节**：

- step 3 的 dismount 是真正"强制"的地方：内核会让该卷的所有现存句柄变成 invalid，应用下一次 ReadFile/WriteFile 直接得 ERROR_INVALID_HANDLE，未刷的写缓冲全丢。这就是为什么需要 2s 延时和明确风险提示。
- step 2 失败不阻断：lock 失败说明仍有句柄，但 dismount 仍然能强行执行（FORCE_OPLOCK_BREAK 的语义）。
- 操作完成后立即触发 DeviceWatcher 刷新（设备节点会被 PnP 移除），列表里盘符自动消失。

---

## 3. 进程关闭的四层方法模型

### 3.1 层级表

| 层级 | 方法 | 适用 | 用户感知 | 数据风险 | 实现 |
|:-:|---|---|---|:-:|---|
| **L1** | 在资源管理器中定位进程 | 任何 | 弹出 explorer 窗口高亮 .exe | 无 | `Process.Start("explorer.exe", "/select,...")` |
| **L2** | WM_CLOSE 优雅关闭 | 有顶层窗口的 GUI 进程 | 应用弹"是否保存" | 极低 | EnumWindows + PostMessage(WM_CLOSE) + WaitForSingleObject |
| **L3** | Restart Manager 关闭 | 向 RM 注册的进程（Office、资源管理器…） | 应用收到 RM shutdown 通知 | 低（RM 协议保证） | RmStartSession + RmRegisterResources + RmShutdown(0) |
| **L4** | TerminateProcess 强制 | 任何同用户进程 | **无任何提示**直接退出 | **高** | Process.Kill() |

### 3.2 默认行为

- L1：永远可用，不需要 `AllowProcessTermination` 闸门
- L2：需要 `AllowProcessTermination = true`
- L3：需要 `AllowProcessTermination = true`，且进程在最近一次 RM 扫描结果中
- L4：需要 `AllowProcessTermination = true` AND `EnableForceTerminate = true`，**且**通过 ConfirmTerminateDialog 二次确认

### 3.3 关键进程保护（双重门）

**第一道**：现有 `CriticalProcesses` 静态黑名单（13 项），永远拒绝 L2/L3/L4，UI 上禁用相关按钮。

**第二道**：新增 `ProcessRiskTier` 三级语义：

```csharp
public enum ProcessRiskTier
{
    Critical,   // 系统不可缺；UI 拒绝任何关闭操作
                // 名单：System / System Idle Process / csrss / wininit / winlogon /
                //      services / lsass / svchost / smss / spoolsv / dwm / audiodg /
                //      MemCompression / Registry
    High,       // 操作有显著副作用；强制结束需"打字精确匹配进程名"二次确认
                // 名单：explorer / Defender 相关 (MsMpEng/SecurityHealthService) /
                //      backup agents / 数据库守护 / 杀软进程 / git daemon
    Normal      // 普通用户进程；强制结束需"勾选我已了解"+ 点确认即可
                // 默认情况；含 notepad / chrome / cmd / pwsh / 我们自己 / 第三方应用
}
```

`explorer.exe` 是 USB 弹出失败最常见元凶之一，但杀掉会让任务栏闪一下（Windows 自动重启），列入 High 等级单独提示。

---

## 4. 数据模型 & 服务接口

### 4.1 新增模型

```csharp
public sealed record TerminationResult(
    bool Success,
    string Method,            // "Reveal" / "WM_CLOSE" / "RestartManager" / "TerminateProcess" /
                              // "Refused-Critical" / "Refused-NoConsent" / "Timeout" / "AlreadyExited"
    string Reason,            // 用户可读说明
    int ExitCode = 0,
    TimeSpan Duration = default);

public sealed record ForceEjectResult(
    bool Success,
    string Stage,             // "OpenVolume" / "Lock" / "Dismount" / "Eject" / "Verify"
    string Reason,
    int Win32Error = 0);
```

### 4.2 新增接口

```csharp
namespace UsbEjectHelper.Core.Abstractions;

public interface IProcessTerminator
{
    bool RevealInExplorer(int pid);

    TerminationResult TryCloseGracefully(int pid, TimeSpan timeout, CancellationToken ct = default);

    TerminationResult TryCloseViaRestartManager(int pid, CancellationToken ct = default);

    TerminationResult ForceTerminate(int pid);

    /// <summary>批量优雅关闭，按 PID 分别返回结果。不抛异常。</summary>
    IReadOnlyList<TerminationResult> CloseManyGracefully(
        IEnumerable<int> pids, TimeSpan perProcessTimeout, CancellationToken ct = default);
}

public interface IForceEjectService
{
    /// <summary>
    /// 强制弹出：lock(best-effort) → dismount → eject。
    /// 任何持有该卷句柄的进程在 dismount 后将得到 invalid handle 错误，可能丢数据。
    /// </summary>
    ForceEjectResult ForceEject(string driveLetter, CancellationToken ct = default);
}

public interface IActionAuditLog
{
    void Append(AuditEntry entry);
}

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Action,            // "close-graceful" / "close-rm" / "force-terminate" / "force-eject"
    int? Pid,
    string? ProcessName,
    string? ExecutablePath,
    string? Drive,
    string? FilePath,
    string Method,
    bool Success,
    string Reason,
    long DurationMs,
    string Consent);          // "auto" / "checkbox" / "type-match" / "force-eject-2s-confirm" / "declined"
```

### 4.3 扩展现有

- `ProcessInspector` 加 `GetRiskTier(string processName) → ProcessRiskTier` 静态方法
- `ProcessInfo` 加 `RiskTier` 属性，替代当前的 `IsCriticalProcess`（后者保留为 `RiskTier == Critical` 的快捷别名以保兼容性）
- `ProcessInfo.CanTerminate` 不再硬编码 `false`，而是返回 `RiskTier != Critical`

---

## 5. 设置项（追加到 `AppSettings`）

| Key | 类型 | 默认 | 含义 |
|---|---|:-:|---|
| `AllowProcessTermination` | bool | **false** | L2/L3/L4 总闸门；首次开启走二次确认（同 `EnableDeepHandleScan` pattern） |
| `EnableForceTerminate` | bool | **false** | L4 单独闸门；只有它打开"强制结束"才作为按钮出现 |
| `EnableForceEject` | bool | **false** | 强制弹出按钮的总闸门；首次开启走二次确认 |
| `GracefulCloseTimeoutSeconds` | int | 5 | L2 等待退出的超时（1~30） |
| `EnableActionAuditLog` | bool | **true** | 默认开启审计日志；可关 |
| `AuditLogMaxSizeMB` | int | 1 | 单文件大小阈值 |
| `AuditLogMaxFiles` | int | 5 | 滚动保留份数 |

`AllowProcessTermination` 和 `EnableForceEject` 两个开关都遵循"首次开启 → 二次确认对话框 → 接受才生效"的 pattern。这两个对话框的文案应当**显著不同**，明确每个开关具体放开了什么能力。

---

## 6. UI 设计

### 6.1 弹出失败对话框 `EjectFailureDialog`（新）

替代现有 `MainWindow.OnEject` 里的 `MessageBox.Show("...是否立即扫描占用进程？", YesNo)`。

```text
┌──────────────────────────────────────────────────────┐
│  ⚠ 无法弹出 E:                                        │
│  原因：设备繁忙（PNP_VetoOutstandingOpen）           │
│                                                      │
│  请选择如何继续：                                    │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ ① 扫描占用进程                                  │  │
│  │   查看是哪个程序在占用，由你决定如何处理        │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ ② 关闭占用进程（之后由你手动重试弹出）          │  │
│  │   通过 WM_CLOSE 优雅关闭。如果应用弹"是否保存"  │  │
│  │   对话框，请先处理它，再点"弹出"按钮。          │  │
│  │                                                │  │
│  │   ⚠ 需要先在设置里开启"允许结束进程"           │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ 🔴 ③ 强制弹出（有数据丢失风险）                 │  │
│  │   跳过文件系统正常卸载流程，直接卸载该卷。      │  │
│  │   持有 E: 上文件的应用会突然失去句柄，未保存的  │  │
│  │   写入数据将丢失。                              │  │
│  │                                                │  │
│  │   ⚠ 需要先在设置里开启"允许强制弹出"           │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│                                              [关闭] │
└──────────────────────────────────────────────────────┘
```

样式约束：
- 三个按钮卡片样式（`Panel` 内含 `Label` + `Button`），高度递增视觉重量
- ③ 按钮**红色文字 + 红色边框** (`ForeColor = Color.FromArgb(192, 32, 32); FlatStyle = Flat;`)
- ② / ③ 在对应设置未开启时灰禁用，tooltip 提示去哪里开

### 6.2 关闭进程子对话框 `CloseProcessesDialog`（新）

进入条件：用户点了 ② 且 `AllowProcessTermination=true`。

进入前：先静默触发一次扫描（不弹结果，把数据传给本对话框）。

```text
┌──────────────────────────────────────────────────────┐
│  关闭占用 E: 的进程                                  │
│                                                      │
│  扫描发现以下进程持有 E: 上的文件：                  │
│  ┌────────────────────────────────────────────────┐  │
│  │ ☑ notepad.exe        (9032)   E:\readme.txt   │  │
│  │ ☑ explorer.exe       (4392)   E:\             │  │ ← Tier=High，蓝色背景提示
│  │ ☐ MsMpEng.exe        (1180)   ⚠ 系统关键      │  │ ← Tier=Critical，灰禁用
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  关闭方式：                                          │
│   ⊙ 优雅关闭 (WM_CLOSE)         ← 默认              │
│   ◯ 强制结束 (TerminateProcess)  ← 需 EnableForceTerminate│
│                                                      │
│  超时：[5] 秒                                       │
│                                                      │
│                          [取消]    [开始关闭]        │
└──────────────────────────────────────────────────────┘
```

执行后弹结果摘要：

```text
"已尝试关闭 3 个进程：
  ✓ notepad.exe (9032) — 已退出
  ⏱ explorer.exe (4392) — 超时（应用可能弹了'是否保存'对话框）
  ⊘ MsMpEng.exe (1180) — 关键进程，已跳过

请处理仍在运行的应用后，再点'弹出'按钮重试。"
                                              [确定]
```

### 6.3 强制结束二次确认 `ConfirmTerminateDialog`（新；按用户选 `type_match_high`）

```text
┌──────────────────────────────────────────────────────┐
│  ⚠ 强制结束进程                                       │
│                                                      │
│  即将结束以下进程：                                  │
│    PID:        4392                                  │
│    进程名:     explorer.exe                          │
│    路径:       C:\Windows\explorer.exe               │
│    占用文件:   E:\                                   │
│    风险等级:   ⚠ High （结束后桌面会闪一下；Windows  │
│                 会自动重启 explorer，但任务栏临时消失）│
│                                                      │
│  ⚠ 强制结束会丢失该进程未保存的数据。                │
│                                                      │
│  请输入进程名以确认：                                │
│  [_____________]   ← 必须打字精确匹配 explorer.exe   │
│                                                      │
│         [取消(默认)]    [我已了解风险，强制结束]     │
│                                ↑ 输入匹配后才启用    │
└──────────────────────────────────────────────────────┘
```

Normal 等级时**不**要求打字，改成：

```text
…
☐ 我已了解强制结束 notepad.exe 会丢失未保存的数据
…
         [取消(默认)]    [强制结束]
                            ↑ 勾选后才启用
```

### 6.4 强制弹出风险确认 `ForceEjectConfirmDialog`（新；2s 延时按用户要求）

```text
┌──────────────────────────────────────────────────────┐
│  🔴 强制弹出 E: — 风险确认                            │
│                                                      │
│  强制弹出会：                                        │
│   • 跳过文件系统正常卸载流程                         │
│   • 立即让所有持有 E: 上文件的应用句柄失效           │
│   • 任何未保存到磁盘的写入将丢失                     │
│   • 拷贝中 / 写入中的文件可能损坏                    │
│                                                      │
│  仅在以下情况使用：                                  │
│   ✓ 你确认 U 盘上没有正在被写入的文件                │
│   ✓ 你已经接受了潜在的数据丢失                       │
│                                                      │
│         [取消（默认）]    [确认 (2)]                │
│                                ↑ disabled，2s 倒计时 │
│                                                      │
│  按钮文字变化：                                      │
│  T=0   "确认 (2)"  disabled                          │
│  T=1s  "确认 (1)"  disabled                          │
│  T=2s  "强制弹出"  红色，enabled                     │
└──────────────────────────────────────────────────────┘
```

**实现要点**：
- 用 `System.Windows.Forms.Timer` Interval=1000ms
- ESC / 取消按钮 / 关闭叉 → 立即取消计时器并 `DialogResult.Cancel`
- 鼠标快速点击穿透：在 disabled 状态下原生 Button 不响应 click 事件，OK
- 倒计时期间用户切走窗口（失焦）也不暂停 —— 简单可预期

### 6.5 主窗口改动

- `OnEject` 失败分支替换为打开 `EjectFailureDialog`，根据返回值分发到现有 OnScan、新增 CloseProcessesDialog、新增 ForceEjectConfirmDialog
- `_resultListView` 新增 `ContextMenuStrip`：
  - "在资源管理器中定位 (Ctrl+E)" — L1
  - "尝试优雅关闭 (Enter)" — L2
  - "强制结束进程..." — L4
  - 分隔线
  - "复制进程路径"
- 主窗口按钮组**不变**（不加新按钮，避免拥挤；进程关闭只通过失败对话框 + 右键菜单进入）

### 6.6 设置对话框改动

`SettingsForm` 增加新分组：

```text
—— 高级（涉及结束进程 / 强制弹出）——
[☐] 允许在程序内结束占用进程
   首次开启会显示一次告知对话框。
[☐] 启用"强制结束"选项（TerminateProcess）
   未保存数据会丢失。
[☐] 允许"强制弹出"
   首次开启会显示一次告知对话框。
优雅关闭超时：[5] 秒
[☑] 启用动作审计日志
   日志位置: %LOCALAPPDATA%\UsbEjectHelper\actions.log
```

`SettingsForm` 整体高度从 400 增到 540，以容纳新分组。

---

## 7. 审计日志设计

文件：`%LOCALAPPDATA%\UsbEjectHelper\actions.log`
格式：JSON Lines，UTF-8 无 BOM，行尾 `\n`
轮转：单文件超过 1MB → 重命名为 `actions.1.log` → 现有 `.1.log` 推到 `.2.log`，最多保留 5 份

每行 schema：

```jsonc
{
  "ts": "2026-05-09T13:36:00.123Z",
  "action": "close-graceful",
  "pid": 9032,
  "name": "notepad.exe",
  "exe": "C:\\Windows\\notepad.exe",
  "drive": "E:",
  "filePath": "E:\\readme.txt",
  "method": "WM_CLOSE",
  "success": true,
  "reason": "进程在 420ms 内退出",
  "durationMs": 420,
  "consent": "checkbox-graceful"
}
```

`consent` 取值约定：
- `"auto"` — 系统决策（如 RiskTier=Critical 拒绝时）
- `"checkbox-graceful"` — 用户勾选了优雅关闭
- `"checkbox-force"` — Normal 等级用户勾选确认强制
- `"type-match-force"` — High 等级用户打字匹配进程名后确认
- `"force-eject-2s-confirm"` — 用户在 2s 倒计时后点确认强制弹出
- `"declined"` — 用户在确认对话框点取消

`EnablePrivacyMode=true` 时 `exe` / `filePath` 字段按现有 `ExportService` 脱敏规则处理。

---

## 8. 文件改动清单

### 8.1 新增（11 个）

| 路径 | 用途 |
|---|---|
| `Core/Abstractions/IProcessTerminator.cs` | 进程关闭接口 |
| `Core/Abstractions/IForceEjectService.cs` | 强制弹出接口 |
| `Core/Abstractions/IActionAuditLog.cs` | 审计日志接口 |
| `Core/ProcessTerminator.cs` | L1/L2/L4 实现 |
| `Core/ProcessTerminator.RestartManager.cs` | L3 实现（partial class，独立文件便于阅读） |
| `Core/ForceEjectService.cs` | FSCTL_LOCK_VOLUME + FSCTL_DISMOUNT_VOLUME + IOCTL_STORAGE_EJECT_MEDIA |
| `Core/ActionAuditLog.cs` | JSON Lines 写入 + 滚动 |
| `Core/ProcessRiskTier.cs` | 三级枚举 |
| `Native/NativeMethods.User32.cs` | EnumWindows / GetWindowThreadProcessId / PostMessage / IsWindowVisible / IsWindowEnabled |
| `UI/EjectFailureDialog.cs` | 三选一对话框 |
| `UI/CloseProcessesDialog.cs` | 关闭进程列表 + 方式选择 |
| `UI/ConfirmTerminateDialog.cs` | 强制结束二次确认（type-match for High） |
| `UI/ForceEjectConfirmDialog.cs` | 强制弹出风险确认 + 2s 倒计时 |

### 8.2 修改（7 个）

| 路径 | 改动 |
|---|---|
| `Core/ProcessInspector.cs` | 加 `GetRiskTier`，扩展 High 名单，`ProcessInfo.RiskTier`/`CanTerminate` 由静态变动态 |
| `Core/EjectService.cs` | 失败时返回更结构化的 reason，便于 `EjectFailureDialog` 显示原因 |
| `Native/NativeMethods.Ioctl.cs` | 加 `FSCTL_LOCK_VOLUME` / `FSCTL_DISMOUNT_VOLUME` / `IOCTL_STORAGE_EJECT_MEDIA` 常量与 P/Invoke |
| `Settings/AppSettings.cs` | 新增 7 个字段 |
| `App/ServiceComposer.cs` | 装配 `ProcessTerminator` / `ForceEjectService` / `ActionAuditLog` |
| `UI/MainWindow.cs` | `OnEject` 失败分支改造；占用结果列表加 ContextMenuStrip |
| `UI/SettingsForm.cs` | 新增"高级（结束进程 / 强制弹出）"分组 |

### 8.3 测试新增 / 修改（5 个）

| 路径 | 新增测试 |
|---|---|
| `tests/ProcessTerminatorTests.cs` | 关键进程拒绝；闸门拒绝；幂等；超时；批量；mock + Process.Start("cmd.exe") 集成 |
| `tests/ForceEjectServiceTests.cs` | 无效盘符；mock IOCTL；不在真实 U 盘上跑（避免误伤） |
| `tests/ActionAuditLogTests.cs` | 写入 round-trip；JSON Lines 解析；滚动；隐私脱敏 |
| `tests/ProcessInspectorTests.cs`（已存在的扩展）| `GetRiskTier` 对各类进程返回正确分级 |
| `tests/SettingsTests.cs`（已存在的扩展）| 7 个新字段默认值 + round-trip |

### 8.4 文档（4 个）

| 路径 | 改动 |
|---|---|
| `doc/plan.md` | 阶段 2 段落末尾加 `→ 详见 phase2-development-plan.md` |
| `doc/manual-test-plan.md` | 新增 §K 节（K-1..K-7） |
| `README.md` | §4.1 功能列表增补"安全 / 强制关闭进程""强制弹出"；§5 表格不变；§9 配置表新增 7 行 |
| `doc/phase2-development-plan.md` | 本文件；实施完成后顶部加完成状态 |

---

## 9. 测试策略

### 9.1 自动化（追加约 25 个测试）

| 测试 | 方式 |
|---|---|
| `IsCriticalProcessName` 现有 13 项命中 | 单测 |
| `GetRiskTier(System)` → Critical | 单测 |
| `GetRiskTier(explorer)` → High | 单测 |
| `GetRiskTier(notepad)` → Normal | 单测 |
| `ForceTerminate(System)` → Refused-Critical | 单测 |
| `ForceTerminate(notepad PID)` 在 `AllowProcessTermination=false` 下 → Refused-NoConsent | 单测 |
| `TryCloseGracefully` 启动子 cmd.exe → WM_CLOSE → 退出 | **集成**（Process.Start("cmd /c timeout 60")） |
| `TryCloseGracefully` 启动无窗口的 dotnet → 立即返回 NoWindow | 集成 |
| `ForceTerminate` 启动子 cmd.exe → kill | 集成 |
| `RevealInExplorer` 不抛异常 | 集成 |
| `CloseManyGracefully` 5 个 PID（含 1 个已退出 + 1 个 Critical PID）→ 各自正确结果 | 集成 |
| `ForceEjectService.ForceEject("Z:")` 无效盘符 → 失败 + 明确错误 | 单测 |
| `ForceEjectService` mock IOCTL 失败 → 返回 Stage="Dismount" | 单测 |
| `ActionAuditLog.Append` × 100 → 文件存在 + 100 行 | 单测 |
| `ActionAuditLog` 1.1MB 触发滚动 → `.1.log` 存在 | 单测 |
| `ActionAuditLog` 隐私模式开启时 exe / filePath 已脱敏 | 单测 |
| `AppSettings` 7 个新字段默认值 | 单测 |
| `AppSettings` round-trip 含新字段 | 单测 |
| `EjectFailureDialog` 三按钮 enabled 状态根据设置正确 | UI 单测（构造对话框验证按钮状态） |
| `ForceEjectConfirmDialog` 倒计时 2s 后按钮 enabled | UI 单测（控制 Timer mock） |
| `ConfirmTerminateDialog` 打字不匹配 → 按钮 disabled | UI 单测 |

预计完成后总数从 89 → ~115，全部跑完仍应在 10s 以内（有几个集成测试要起子进程）。

### 9.2 手动测试（追加 §K，写入 manual-test-plan.md）

| 编号 | 步骤 | 预期 |
|---|---|---|
| **K-1** | notepad 打开 U 盘文件 → 弹出 → 选②"关闭占用" → 在子对话框默认勾选 + 优雅关闭 | notepad 弹"是否保存"；用户点"不保存" → notepad 退出 → 主窗口提示"X 个已退出" |
| **K-2** | 同上但用户在保存对话框点"取消" | 5s 超时 → 主窗口提示"应用可能弹了保存对话框" → 用户处理后手动重试弹出 → 弹出成功 |
| **K-3** | notepad 持文件 → 弹出 → 选 ② → 子对话框切到"强制结束"+ 输入"notepad.exe" → 确认 | notepad 立即消失 → 主窗口 |
| **K-4** | 占用结果里手工塞 PID=4 / explorer → 右键 | PID=4 三个关闭项灰；explorer 强制结束需要打字"explorer.exe"才能点 |
| **K-5** | 弹出 → 选 ③ "强制弹出" | 风险对话框出现 → "确认 (2)" 灰 → 1s 后 "确认 (1)" 灰 → 2s 后红色"强制弹出"亮 → 点 → 卷消失 |
| **K-6** | 同 K-5 但在 1.5s 时点 ESC | 立即取消，无任何动作；audit log 无 force-eject 条 |
| **K-7** | 检查 `%LOCALAPPDATA%\UsbEjectHelper\actions.log` | 每个动作一行 JSON；启用隐私模式后 `exe` / `filePath` 已脱敏 |

---

## 10. 风险点（提前认领）

1. **WM_CLOSE 对无窗口进程无效**
   后台服务、`dotnet` host、cmd 子进程没有顶层窗口，L2 直接 timeout。`TryCloseGracefully` 内部探测：`EnumWindows` 没找到该 PID 的窗口就直接返回 `Method="WM_CLOSE-NoWindow"`。

2. **WM_CLOSE 被进程拒绝**
   弹了"是否保存"用户点取消 → 进程没退 → 超时返回。这是预期行为，不算 bug；对用户的提示要明确这种可能。

3. **TerminateProcess 跨用户失败**
   同用户 / 同完整性以下能成功；管理员重启后能多覆盖。第一版不做提权流程，失败时给"以管理员身份重启可能成功"提示。

4. **进程在二次确认期间退出**
   幂等处理：`Process.GetProcessById(pid)` 抛 `ArgumentException` → 返回 `Success=true, Method="AlreadyExited"`。

5. **explorer.exe 杀掉桌面消失**
   Windows 自动重启 explorer，但任务栏闪一下。在 ConfirmTerminateDialog High 横幅里明示。

6. **杀软 / EDR 拦截 OpenProcess**
   部分 EDR 拦截非白名单进程的 OpenProcess+TerminateProcess。失败时给明确错误（"被本机安全软件拦截"），不重试。

7. **审计日志膨胀**
   写满磁盘。已设计滚动：单文件 1MB，保留 5 份，最多 5MB。

8. **强制弹出后应用崩溃**
   FSCTL_DISMOUNT_VOLUME 让句柄 invalidate，应用下一次 IO 出错。某些应用会崩溃（弹未处理异常），某些会优雅显示错误。这不属于本工具的 bug，但风险确认对话框要写清楚。

9. **强制弹出对系统盘 / 固定硬盘**
   `IForceEjectService.ForceEject` 必须先验证盘符是 USB removable（复用 `DeviceWatcher` 的设备类型）。**绝不**对 C: 或固定硬盘执行 dismount，万一执行了会让系统假死。

10. **2s 延时按钮被 Enter 键绕过**
    `Form.AcceptButton` 默认会让 Enter 触发 OK。`ForceEjectConfirmDialog` 不设 `AcceptButton`，让 Enter 不触发。Tab + Space 也只在按钮 enabled 后才生效（Windows 原生行为）。

---

## 11. 实施计划

按用户选择 **一个大 PR（PR10）一次性提交**。开发顺序（自底向上）：

1. **底层 P/Invoke**：`NativeMethods.User32.cs` + `NativeMethods.Ioctl.cs` 加常量
2. **关键模型**：`ProcessRiskTier.cs` / `TerminationResult.cs` / `ForceEjectResult.cs` / `AuditEntry.cs`
3. **`ProcessInspector` 扩展**：`GetRiskTier` + High 名单
4. **服务实现**：`ProcessTerminator` / `ForceEjectService` / `ActionAuditLog`
5. **`AppSettings` 字段** + `ServiceComposer` 装配
6. **测试** ：`ProcessTerminatorTests` / `ForceEjectServiceTests` / `ActionAuditLogTests` —— 此时业务层应该独立可测
7. **UI 层**：`EjectFailureDialog` / `CloseProcessesDialog` / `ConfirmTerminateDialog` / `ForceEjectConfirmDialog`
8. **`MainWindow.OnEject` 改造** + 右键菜单
9. **`SettingsForm` 加分组**
10. **文档**：`manual-test-plan.md` §K / `README.md` §4 §9 / `plan.md` 加跳转
11. **构建 + 全量测试 + smoke test**
12. **PR10 提交**

预计代码量：~1500 ~ 2000 行新代码（不含测试）；~600 ~ 800 行测试；~100 行文档增补。

---

## 12. 完成状态

> 待实施。完成后在此处填写：
> - 实际新增 / 修改文件数
> - 新增 / 修改单测数 + 全量测试运行时间
> - smoke test 结果
> - 已知遗留问题（推到阶段 3）
